# LaunchForge Terminal Integration

This local companion extension receives project actions from LaunchForge and executes them as VS Code tasks in the correct workspace's integrated terminal. It preserves existing `tasks.json` files.

The first launch asks you to approve the exact action. Choose **Always allow this action** to remember it. Changing the command or folder requires a new approval. Tasks only run in trusted workspaces.

This version supports local desktop VS Code and its default profile. Remote/WSL workspaces and VS Code Insiders are not supported. LaunchForge installs this bundled package on first use when you select **VS Code terminal**. It is not published to the Marketplace.
