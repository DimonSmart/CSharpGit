cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.43"

  sha256 arm: "3c904ddb191f922ca611e4be2bcd54c245af87aff2d89f82720e1667843edd7a",
         intel: "180c66881eab0dbd7372c18eb7c4662c86c713b63ed517ea5ba386c59672dafc"

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

