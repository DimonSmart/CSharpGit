cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.24"

  sha256 arm: "050b3eb9b533a29de4c06ad59c0e5105d210859733e17f0e29e43697bb7252b4",
         intel: "fdf69bb654093e3a018da5661867b832481174809c2a65e6cac9ffe8350e4f15"

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

