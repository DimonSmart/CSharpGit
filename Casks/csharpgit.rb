cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.49"

  sha256 arm: "87452ae555099f2d25a20df61cfd982ce5232e24d433597ec3c984e961292b4d",
         intel: "35141559788e155d87c29176e115e7ccefcac4bb9b5d6a0690e0808d1e2f7794"

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

