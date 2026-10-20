cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.36"

  sha256 arm: "b6a08eb2703bec50a0037aad1409499cfca53b1cf897a0b1df345296a792ef2d",
         intel: "bb5d145f995cdadf4667fddda56cd6377c8142ba2fed094bdccf8e7c9724d0ee"

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

