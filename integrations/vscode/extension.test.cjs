const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const folder = path.resolve('test-project');
const source = fs.readFileSync(path.join(__dirname, 'vsix/extension/extension.js'), 'utf8');

function harness({ trusted = true, currentFolder = folder, choices = ['Run once'] } = {}) {
  const state = new Map();
  const calls = { tasks: [], commands: [], errors: [], approvals: 0 };
  const callbacks = {};
  const vscode = {
    workspace: {
      isTrusted: trusted,
      workspaceFolders: [{ name: 'Project', uri: { fsPath: currentFolder, scheme: 'file' } }],
      onDidGrantWorkspaceTrust: callback => { callbacks.trust = callback; return {}; },
      onDidChangeWorkspaceFolders: callback => { callbacks.folders = callback; return {}; }
    },
    window: {
      registerUriHandler: handler => { callbacks.uri = handler.handleUri; return {}; },
      showInformationMessage: async (_message, options) => { if (options?.modal) { calls.approvals++; return choices.shift(); } },
      showErrorMessage: async message => calls.errors.push(message)
    },
    commands: { executeCommand: async (...args) => calls.commands.push(args) },
    Uri: { file: fsPath => ({ fsPath }) },
    ShellExecution: class { constructor(...args) { this.arguments = args; } },
    Task: class { constructor(definition, scope, name, owner, execution) { Object.assign(this, { definition, scope, name, owner, execution }); } },
    TaskRevealKind: { Always: 1 }, TaskPanelKind: { Dedicated: 2 },
    tasks: { executeTask: async task => calls.tasks.push(task) }
  };
  const module = { exports: {} };
  vm.runInNewContext(source, { module, require: name => name === 'vscode' ? vscode : require(name), Buffer, URLSearchParams, process });
  const context = { subscriptions: [], globalState: { get: (key, fallback) => state.get(key) ?? fallback, update: async (key, value) => state.set(key, value) } };
  module.exports.activate(context);
  const send = async changes => {
    const request = { version: 1, folder, name: 'Run project', executable: 'npm', args: ['run', 'dev'], command: null, ...changes };
    await callbacks.uri({ authority: 'launchforge.launcher', path: '/run', query: 'request=' + Buffer.from(JSON.stringify(request)).toString('base64url') });
  };
  return { send, calls, callbacks, state, vscode, context, extension: module.exports };
}

test('guided project action uses a VS Code task in the correct workspace', async () => {
  const h = harness(); await h.send();
  assert.equal(h.calls.tasks.length, 1);
  const task = h.calls.tasks[0];
  assert.equal(task.scope.uri.fsPath, folder);
  assert.equal(task.execution.arguments[0], 'npm');
  assert.equal(JSON.stringify(task.execution.arguments[1]), JSON.stringify(['run', 'dev']));
  assert.equal(task.execution.arguments[2].cwd, folder);
});

test('custom commands stay available inside the VS Code terminal', async () => {
  const h = harness(); await h.send({ command: 'echo hello && echo world' });
  assert.equal(h.calls.tasks[0].execution.arguments[0], 'echo hello && echo world');
});

test('declining an external launch request runs nothing', async () => {
  const h = harness({ choices: [] }); await h.send(); assert.equal(h.calls.tasks.length, 0);
});

test('approval is remembered for the exact action and invalidated on a change', async () => {
  const h = harness({ choices: ['Always allow this action', 'Run once'] });
  await h.send(); await h.send(); await h.send({ args: ['run', 'test'] });
  assert.equal(h.calls.tasks.length, 3); assert.equal(h.calls.approvals, 2);
});

test('an unmatched folder opens a workspace and resumes its pending task', async () => {
  const h = harness({ currentFolder: path.resolve('other-project') }); await h.send();
  assert.equal(h.calls.tasks.length, 0); assert.equal(h.calls.commands[0][0], 'vscode.openFolder');
  h.vscode.workspace.workspaceFolders = [{ name: 'Project', uri: { fsPath: folder, scheme: 'file' } }];
  h.extension.activate(h.context);
  await h.callbacks.folders();
  assert.equal(h.calls.tasks.length, 1); assert.equal(h.state.get('pendingLaunches').length, 0);
});

test('an untrusted workspace waits for trust before running', async () => {
  const h = harness({ trusted: false }); await h.send(); assert.equal(h.calls.tasks.length, 0);
  h.vscode.workspace.isTrusted = true; await h.callbacks.trust(); assert.equal(h.calls.tasks.length, 1);
});

test('invalid URI and protocol requests do not execute', async () => {
  const h = harness(); await h.send({ version: 99 });
  assert.equal(h.calls.tasks.length, 0); assert.equal(h.calls.errors.length, 1);
  assert.throws(() => h.extension.parseRequest({ authority: 'other.extension', path: '/run', query: '' }));
});
