'use strict';
const vscode = require('vscode');
const path = require('node:path');
const crypto = require('node:crypto');

function parseRequest(uri) {
  if (uri.authority !== 'launchforge.launcher' || uri.path !== '/run') throw new Error('Unsupported Lazy Launcher request.');
  const encoded = new URLSearchParams(uri.query).get('request');
  if (!encoded || encoded.length > 90000) throw new Error('Invalid launch request.');
  const request = JSON.parse(Buffer.from(encoded, 'base64').toString('utf8'));
  if (request.version !== 1 || typeof request.folder !== 'string' || !path.isAbsolute(request.folder) || typeof request.name !== 'string') throw new Error('Invalid project information.');
  if (request.command !== null && request.command !== undefined) {
    if (typeof request.command !== 'string' || !request.command.trim()) throw new Error('The custom command is empty.');
  } else if (typeof request.executable !== 'string' || !request.executable || !Array.isArray(request.args) || request.args.some(arg => typeof arg !== 'string')) throw new Error('Invalid project task.');
  return request;
}

function sameFolder(left, right) {
  const normalize = value => process.platform === 'win32' ? path.resolve(value).toLowerCase() : path.resolve(value);
  return normalize(left) === normalize(right);
}

function activate(context) {
  let queue = Promise.resolve();
  async function run(request) {
    const workspace = vscode.workspace.workspaceFolders?.find(folder => sameFolder(folder.uri.fsPath, request.folder));
    if (!workspace) {
      const pending = context.globalState.get('pendingLaunches', []);
      await context.globalState.update('pendingLaunches', [...pending, request].slice(-20));
      await vscode.commands.executeCommand('vscode.openFolder', vscode.Uri.file(request.folder), { forceNewWindow: true });
      return;
    }
    if (!vscode.workspace.isTrusted) {
      const pending = context.globalState.get('pendingLaunches', []);
      await context.globalState.update('pendingLaunches', [...pending, request].slice(-20));
      await vscode.window.showInformationMessage('Trust this project in VS Code to run its Lazy Launcher task.');
      return;
    }
    // URI handlers accept external input. Approval is tied to the exact task and folder.
    const fingerprint = crypto.createHash('sha256').update(JSON.stringify(request)).digest('hex');
    const approved = context.globalState.get('approvedLaunches', []);
    if (!approved.includes(fingerprint)) {
      const detail = request.command || [request.executable, ...request.args].join(' ');
      const choice = await vscode.window.showInformationMessage(`Run “${request.name}” in ${workspace.name}?`, { modal: true, detail }, 'Run once', 'Always allow this action');
      if (!choice) return;
      if (choice === 'Always allow this action') await context.globalState.update('approvedLaunches', [...approved, fingerprint].slice(-200));
    }
    const options = { cwd: request.folder };
    const execution = request.command
      ? new vscode.ShellExecution(request.command, options)
      : new vscode.ShellExecution(request.executable, request.args, options);
    const task = new vscode.Task({ type: 'launchforge', id: fingerprint }, workspace, request.name, 'Lazy Launcher', execution, []);
    task.presentationOptions = { reveal: vscode.TaskRevealKind.Always, panel: vscode.TaskPanelKind.Dedicated, focus: true, clear: false };
    await vscode.tasks.executeTask(task);
  }
  async function resumePending() {
    const pending = context.globalState.get('pendingLaunches', []);
    const matching = pending.filter(request => vscode.workspace.workspaceFolders?.some(folder => sameFolder(folder.uri.fsPath, request.folder)));
    if (!matching.length || !vscode.workspace.isTrusted) return;
    await context.globalState.update('pendingLaunches', pending.filter(request => !matching.includes(request)));
    for (const request of matching) await run(request);
  }
  function enqueue(work) {
    queue = queue.then(work).catch(error => vscode.window.showErrorMessage(`Lazy Launcher: ${error.message}`));
    return queue;
  }
  context.subscriptions.push(vscode.window.registerUriHandler({ handleUri: uri => enqueue(() => run(parseRequest(uri))) }));
  context.subscriptions.push(vscode.workspace.onDidGrantWorkspaceTrust(() => enqueue(resumePending)));
  context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders(() => enqueue(resumePending)));
  void enqueue(resumePending);
}

module.exports = { activate, parseRequest };
