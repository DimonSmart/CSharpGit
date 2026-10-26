cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.40"

  sha256 arm: "066bca110e04c4585c19d0f796ed4d6f8d0cdf12c84bad5867d7d35aafaf5cac",
         intel: "06ba141d232cfb88b11ff68fa08fb2b2a932dae9f02dd7092033e934445d8843"

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

