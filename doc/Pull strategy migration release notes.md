# Pull strategy migration — release notes

- The Pull button now displays the application's default integration strategy and the current branch's behind count when available.
- The Pull menu contains three explicit one-time actions (Fast-forward only, Merge, Rebase) and a shortcut to Settings → General → Git pull. One-time Pull does not change the default.
- New installations default to **Fast-forward only**. Existing settings with **Git configuration** (legacy value 0) also migrate to **Fast-forward only** on loading. **This changes behavior:** diverged branches now refuse Pull until you choose Merge or Rebase, rather than implicitly using Git's `pull.rebase` or `pull.ff` setting to pick an integration.
- Previously saved Merge, Rebase and Fast-forward only preferences are preserved. Git config files are not modified.
- The explicit Merge strategy uses `git pull --prune --no-rebase --ff`, allowing ordinary fast-forward even with `pull.ff=false`.
- The Force Git autostash setting is still available in Pull settings and keeps its previous semantics.
