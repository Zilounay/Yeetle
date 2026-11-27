# Releasing Yeetle

How a version reaches testers and users (spec §12, #22).

## Versions

- Plain `X.Y.Z`, no `-beta`: the first release is **0.0.1**, then the minor and major numbers climb.
- Every 0.x release is a beta. **1.0.0** is the public release.
- `VersionPrefix` in `Directory.Build.props` is the version being built between releases. The file version adds the CI run number: `0.0.1.<build>` (`0` for local builds).
- A release takes its version from its tag; after a release, raise `VersionPrefix` to the next version in the next pull request.

## Publishing a release

1. Merge everything for the release into `main`; the Build workflow must be green.
2. Tag `main` and push the tag:

   ```
   git tag v0.0.1
   git push origin v0.0.1
   ```

3. The **Release** workflow (`.github/workflows/release.yml`) builds, tests, publishes a self-contained win-x64 build, packs it with Velopack and publishes it on GitHub Releases as "Yeetle 0.0.1".
4. Testers install with `YeetleApp-win-Setup.exe` from the release page. MVP builds are unsigned, so Windows SmartScreen warns: More info → Run anyway.

## Release gate

- 0.x tags always go out (betas). Before each one, remind ZiL of any open `release blocker` issue (#67, #69): the beta will not have it.
- Tags of 1.0.0 and later fail while any open issue carries the `release blocker` label. Only ZiL waives a blocker, by removing its label (CLAUDE.md, Workflow).

## How it installs and updates

- Per user, no administrator rights. The app goes to `%LOCALAPPDATA%\YeetleApp\`; shortcuts and Apps & features say "Yeetle".
- User data stays in `%LOCALAPPDATA%\Yeetle\` (database, settings, tags, backups, logs). Updates and uninstalling never touch it: the different folder name is what keeps an uninstall from deleting it.
- At startup an installed Yeetle checks GitHub Releases in the background. When a newer version exists, the top bar shows **Update to X.Y.Z**: one click (after Save / Discard if a session runs) downloads it and restarts into it. A failed check only goes to the log.
- A copy run from a build folder (Rider, `dotnet run`) is not installed and never checks.
