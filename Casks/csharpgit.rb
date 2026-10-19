cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.35"

  sha256 arm: "71b4f781f1a477063913513d5370b550f00e9ef156d53b265b93f74ddf3426e4",
         intel: "7b91a52c1cb7eae5a2a7fa107199d169e9a86f08f38067315514913b676d3a3d"

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

