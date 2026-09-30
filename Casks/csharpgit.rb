cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.18"

  sha256 arm: "c20fd60019b41e7c6d2159d4ddf0ce36be2049cdde65fd4e56babe5e763cbfac",
         intel: "46ff5f697168d6df74221be789d5637cc4f65e49897b760dfe6fbffd82182185"

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

