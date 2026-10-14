cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.32"

  sha256 arm: "aa7261610a0ba1baec7eab27224ecdb8c9bb1c17d858ff10ca51867f4da5651f",
         intel: "ff80a8c5088232ca61d8fe646c6d02e6d04e4534f210bb19f566b314d6605622"

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

