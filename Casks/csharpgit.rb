cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.52"

  sha256 arm: "6fc18b0d456970698492150a53466a1b6211c459443a362e0734f5e001138556",
         intel: "fa7c2745f9e295a03d1fbc1fdbeec6c7ca3c0277ef49cee5686db5599f941839"

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

