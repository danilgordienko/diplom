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
let out: vscode.OutputChannel;

export function activate(context: vscode.ExtensionContext) {

    // Создаём канал вывода — он сразу появится в списке Output
    out = vscode.window.createOutputChannel('Pascal Language Server');
    context.subscriptions.push(out);
    out.show(true); // открыть автоматически

    out.appendLine('=== Pascal LSP активируется ===');
    out.appendLine(`extensionPath: ${context.extensionPath}`);

    // ── Определяем путь к серверу ────────────────────────────────────────────

    const config     = vscode.workspace.getConfiguration('pascal-lsp');
    const serverPath = config.get<string>('serverPath') ?? '';

    out.appendLine(`Настройка serverPath: "${serverPath}"`);

    // Вариант 1: путь задан явно в настройках
    let exePath = serverPath;

    // Вариант 2: exe лежит рядом с папкой расширения (режим F5 / структура проекта)
    if (!exePath || !fs.existsSync(exePath)) {
        const candidate = path.join(context.extensionPath, '..', 'bin', 'Debug', 'net8.0', 'test.exe');
        out.appendLine(`Проверяю кандидат (рядом с расширением): ${candidate}`);
        if (fs.existsSync(candidate)) {
            exePath = candidate;
        }
    }

    const useExe = !!exePath && fs.existsSync(exePath);
    out.appendLine(`exe найден: ${useExe}  путь: ${exePath || '(не найден)'}`);

    let serverOptions: ServerOptions;

    if (useExe) {
        out.appendLine(`Запускаю: ${exePath}`);
        serverOptions = {
            command: exePath,
            args: ['--lsp'],
            transport: TransportKind.stdio,
        };
    } else {
        // Fallback: dotnet run
        const csharpProject = path.join(context.extensionPath, '..');
        out.appendLine(`exe не найден, пробую dotnet run в: ${csharpProject}`);

        // Проверяем есть ли вообще csproj
        const csproj = fs.readdirSync(csharpProject).find(f => f.endsWith('.csproj'));
        if (!csproj) {
            const msg = `Не найден ни test.exe, ни .csproj в ${csharpProject}.\n` +
                        `Укажи путь к серверу в настройках: pascal-lsp.serverPath`;
            out.appendLine('ОШИБКА: ' + msg);
            vscode.window.showErrorMessage('Pascal LSP: ' + msg);
            return;
        }

        out.appendLine(`Найден проект: ${csproj}, запускаю dotnet run`);
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
        outputChannel: out,   // логи сервера тоже идут в наш канал
    };

    // ── Запуск ───────────────────────────────────────────────────────────────

    client = new LanguageClient(
        'pascal-lsp',
        'Pascal Language Server',
        serverOptions,
        clientOptions,
    );

    out.appendLine('Запускаю LanguageClient...');

    client.start().then(() => {
        out.appendLine('LanguageClient запущен успешно.');
    }).catch((err: unknown) => {
        const msg = err instanceof Error ? err.message : String(err);
        out.appendLine('ОШИБКА запуска LanguageClient: ' + msg);
        vscode.window.showErrorMessage('Pascal LSP не запустился: ' + msg);
    });

    // Статус-бар
    const statusBar = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 0);
    statusBar.text    = '$(symbol-misc) Pascal LSP';
    statusBar.tooltip = useExe ? `Сервер: ${exePath}` : 'Сервер: dotnet run';
    statusBar.command = 'pascal-lsp.showOutput';
    statusBar.show();
    context.subscriptions.push(statusBar);

    // Команда: клик на статус-бар → открыть Output
    context.subscriptions.push(
        vscode.commands.registerCommand('pascal-lsp.showOutput', () => out.show())
    );

    vscode.window.showInformationMessage(
        useExe ? `Pascal LSP: запущен (${path.basename(exePath)})` : 'Pascal LSP: запущен (dotnet run)'
    );
}

export function deactivate(): Thenable<void> | undefined {
    return client?.stop();
}