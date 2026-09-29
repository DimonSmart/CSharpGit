cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.14"

  sha256 arm: "35b9b94a6d89975af3b39d263fe5b2bc26c99a1b0bc9168c6a53316cfdc58abe",
         intel: "b1b4d85488f0838faf59aca9a19a7e941dcb73fc769e6d4756c659e9e19f57f0"

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

