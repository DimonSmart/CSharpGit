# CSharpGit

Кроссплатформенный desktop Git-клиент на .NET 10, Uno Platform и Skia Desktop.

## Требования и запуск

- .NET SDK 10.0.204 или новее в линейке .NET 10;
- Git, доступный через `PATH`;
- нативные зависимости Uno Skia Desktop для целевой ОС.

```powershell
dotnet restore CSharpGit.slnx
dotnet run --project src/CSharpGit.Presentation -f net10.0-desktop
```

Один процесс открывает один репозиторий или worktree. Для другого репозитория
запустите ещё один процесс. Windows и macOS являются основными платформами;
Linux/X11 поддерживается в режиме best effort тем же desktop target.

Конфигурация читается из `appsettings.json` и переменных окружения с префиксом
`CSHARPGIT_`. Журнал пишется локально в консоль;
telemetry и пользовательская аналитика не подключены.
