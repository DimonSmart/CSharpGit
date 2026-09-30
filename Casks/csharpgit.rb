cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.16"

  sha256 arm: "a8ae48011be59daf89df68ce900ac5dcf848eb99ba7d2af7ef366cdec5cc4041",
         intel: "35267d61e1490efe9a11a1f8e2314d30d3fc39a01c420f7aec179b1fd2dc97fc"

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

