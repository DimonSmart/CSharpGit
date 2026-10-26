cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.39"

  sha256 arm: "755ada7a4a87eba4627e2abb30d983581c9aaf75a5d4751afa8111dc4551a0b4",
         intel: "a8c8e5c09ab8db1da30dda9f983669bc7214c93256383109deb3964487eab134"

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

