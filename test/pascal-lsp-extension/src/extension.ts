import * as path from 'path';
import * as fs from 'fs';
import * as vscode from 'vscode';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind,
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;

export function activate(context: vscode.ExtensionContext) {

    // ── Путь к серверу ───────────────────────────────────────────────────────
    //
    // После установки через .vsix расширение живёт в:
    //   %USERPROFILE%\.vscode\extensions\pascal-lsp-0.0.1\
    // и context.extensionPath указывает туда, а не в папку проекта.
    //
    // Поэтому путь к серверу берём из настройки VS Code pascal-lsp.serverPath.
    // Если настройка не задана — используем захардкоженный путь по умолчанию.

    const config     = vscode.workspace.getConfiguration('pascal-lsp');
    const serverPath = config.get<string>('serverPath') ?? '';

    let exePath: string;
    let csharpProject: string;

    if (serverPath && fs.existsSync(serverPath)) {
        // Пользователь явно указал путь к exe в настройках
        exePath       = serverPath;
        csharpProject = path.dirname(path.dirname(path.dirname(path.dirname(serverPath)))); // bin/Debug/net8.0 → вверх 3 раза
    } else {
        // Путь по умолчанию — рядом с папкой расширения
        // Работает когда расширение запускается через F5 из pascal-lsp-extension/
        csharpProject = path.join(context.extensionPath, '..');
        exePath       = path.join(csharpProject, 'bin', 'Debug', 'net8.0', 'test.exe');
    }

    const useExe = fs.existsSync(exePath);

    let serverOptions: ServerOptions;

    if (useExe) {
        serverOptions = {
            command: exePath,
            args: ['--lsp'],
            transport: TransportKind.stdio,
        };
    } else {
        // Fallback: dotnet run — работает без предварительной сборки
        serverOptions = {
            command: 'dotnet',
            args: ['run', '--project', csharpProject, '--', '--lsp'],
            transport: TransportKind.stdio,
        };
    }

    // ── Настройки клиента ────────────────────────────────────────────────────

    const clientOptions: LanguageClientOptions = {
        documentSelector: [
            { scheme: 'file', language: 'pascal' }
        ],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.pas'),
        },
    };

    // ── Запуск ───────────────────────────────────────────────────────────────

    client = new LanguageClient(
        'pascal-lsp',
        'Pascal Language Server',
        serverOptions,
        clientOptions,
    );

    client.start();

    // Статус-бар
    const statusBar = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 0);
    statusBar.text    = '$(symbol-misc) Pascal LSP';
    statusBar.tooltip = useExe
        ? `Сервер: ${exePath}`
        : `Сервер: dotnet run в ${csharpProject}`;
    statusBar.show();
    context.subscriptions.push(statusBar);

    const mode = useExe ? `exe: ${exePath}` : `dotnet run: ${csharpProject}`;
    vscode.window.showInformationMessage(`Pascal LSP запущен (${mode})`);
    console.log(`[pascal-lsp] ${mode}`);
}

export function deactivate(): Thenable<void> | undefined {
    return client?.stop();
}