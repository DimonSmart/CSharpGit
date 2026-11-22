cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.54"

  sha256 arm: "85f77239d4fbcf51a457bd3ef5399f181f57135df736b68bcb1cca98c46b0d6a",
         intel: "0bff8f175e9191c84c343fefc613db9c04f67527ee3bb9a7dd4cdba3a32e3bd4"

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

