cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.28"

  sha256 arm: "cec5228c460a85e5d5eb58a310addf5b8bf126c0b806b93e4e9f87e51335b944",
         intel: "28b041da7ee891ec42a035f72e038f06b20eba0bd1f1d4b6f7a811d99f490048"

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

