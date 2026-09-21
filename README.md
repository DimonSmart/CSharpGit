# CSharpGit

Кроссплатформенный desktop Git-клиент на .NET 10, Uno Platform и Skia Desktop.

Windows и macOS являются основными платформами. Linux/X11 поддерживается в режиме best effort.

## Installation

Для работы готового CSharpGit требуется `git`, доступный через `PATH`.

### Windows

1. Скачайте `CSharpGit-vX.Y.Z-win-x64.zip` из GitHub Releases.
2. Расакуйте архив.
3. Запустите `CSharpGit.exe`.

Windows release является self-contained: отдельная установка .NET Runtime или .NET SDK не требуется.

### macOS

Homebrew Cask — рекомендуемый способ установки CSharpGit на macOS.

Добавьте репозиторий как Homebrew tap один раз:

```bash
brew tap dimonsmart/csharpgit https://github.com/DimonSmart/CSharpGit.git
```

Установите приложение:

```bash
brew install --cask dimonsmart/csharpgit/csharpgit
```

Homebrew автоматически выбирает сборку для Apple Silicon (`arm64`) или Intel (`x64`) и устанавливает `CSharpGit.app` в Applications.

macOS release является self-contained: отдельная установка .NET Runtime или .NET SDK не требуется. Git по-прежнему должен быть установлен и доступен через `PATH`.

Homebrew Cask генерируется из опубликованных GitHub Releases и их SHA-256 checksums, поэтому установка через Homebrew становится доступна после публикации первого release.

Обновление:

```bash
brew update
brew upgrade --cask dimonsmart/csharpgit/csharpgit
```

Удаление:

```bash
brew uninstall --cask dimonsmart/csharpgit/csharpgit
```

#### Manual installation

Также можно скачать из GitHub Releases архив для своей архитектуры:

- Apple Silicon: `CSharpGit-vX.Y.Z-osx-arm64-app.zip`;
- Intel: `CSharpGit-vX.Y.Z-osx-x64-app.zip`.

Распакуйте архив и перенесите `CSharpGit.app` в Applications.

CSharpGit пока unsigned и not notarized, поэтому при первом запуске Gatekeeper может заблокировать приложение. На современных версиях macOS:

1. Попробуйте открыть `CSharpGit.app` и закройте предупреждение кнопкой **Done**.
2. Откройте **System Settings → Privacy & Security** и прокрутите до раздела **Security**.
3. Нажмите **Open Anyway** для CSharpGit и подтвердите действие, если macOS запросит аутентификацию.
4. Ещё раз подтвердите запуск кнопкой **Open**.

Обычно это требуется только при первом запуске.

### Linux

Скачайте `CSharpGit-vX.Y.Z-linux-x64.tar.gz` из GitHub Releases, распакуйте его и запустите:

```bash
./CSharpGit
```

Linux поддерживается в режиме best effort. В зависимости от дистрибутива могут потребоваться системные native/X11 dependencies.

## Development

Для сборки и запуска из исходников нужны:

- .NET SDK 10.0.204 или совместимый patch из линейки .NET 10 согласно `global.json`;
- Git, доступный через `PATH`;
- нативные зависимости Uno Skia Desktop для целевой ОС.

```powershell
dotnet restore CSharpGit.slnx
dotnet run --project src/CSharpGit.Presentation -f net10.0-desktop
```

Один процесс открывает один репозиторий или worktree. Для другого репозитория запустите ещё один процесс.

Конфигурация читается из `appsettings.json` и переменных окружения с префиксом `CSHARPGIT_`. Журнал пишется локально в консоль; telemetry и пользовательская аналитика не подключены.
