cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.10"

  sha256 arm: "6cfc8efd47e3dd708dcf160ffabaf92577e6d2503d5a08a9ea58e8442a2200d1",
         intel: "0c21f05e3926280fa6c410d2be53d7f858437d03c4fe41c0de74200bdafe2e1a"

  url "https://github.com/DimonSmart/CSharpGit/releases/download/v#{version}/CSharpGit-v#{version}-osx-#{arch}-app.zip"

  name "CSharpGit"
  desc "Cross-platform Git client built with C#, .NET and Uno Platform"
  homepage "https://github.com/DimonSmart/CSharpGit"

  app "CSharpGit.app"

  caveats <<~EOS
    CSharpGit is currently unsigned and not notarized.
    On first launch macOS may require using Open from the Finder context menu.
  EOS
end

