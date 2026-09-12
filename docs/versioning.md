# Versioning and releases

## Where the version lives

In exactly one place: `<Version>` in [`Directory.Build.props`](../Directory.Build.props).

The `GenerateBuildInfo` target in the csproj writes `obj/…/BuildInfo.g.cs` before each
compile, and the plugin reads its version from it:

```csharp
public const string PluginVersion = BuildInfo.Version;
```

`BuildInfo.Marker` is `<Version>+<BuildRevision>`. `BuildRevision` defaults to `local`,
and the release script passes the short commit SHA. The marker is logged at startup,
so any DLL in the wild can be traced back to its commit.

## Scheme

[Semantic versioning](https://semver.org/):

| Bump | When |
|---|---|
| **Major** | The plugin GUID or config keys change. |
| **Minor** | New screens or features, or new config keys with safe defaults. |
| **Patch** | Fixes, or following a game update that moved something. |

## Cutting a release

1. Bump `<Version>` in `Directory.Build.props`.
2. Add the entry to [`CHANGELOG.md`](../CHANGELOG.md).
3. Commit, tag with a `v` prefix, and push the tag:

   ```bash
   git commit -am "Release 0.2.0"
   git tag v0.2.0
   git push origin main --tags
   ```

The [`release` workflow](../.github/workflows/release.yml) runs on the tag. It fails if
the tag doesn't match `<Version>`, and otherwise publishes a GitHub release with notes.

### Release assets

CI can't build the DLL, because it has no copy of the game. Build and upload it from a
machine that has one:

```bash
git checkout v0.2.0
scripts/release-dll.sh
```

The script refuses to run on a dirty tree, or when `HEAD` isn't the tagged commit. It
uploads three assets:

| Asset | Contents |
|---|---|
| `JmtLiftoffLeaderboard.dll` | The plugin, to drop straight into `BepInEx\plugins\`. Liftoff Control installs this one. |
| `JmtLiftoffLeaderboard-<version>.zip` | The DLL, pdb, `LICENSE` and install instructions. |
| `SHA256SUMS.txt` | Checksums for both. Liftoff Control verifies the DLL against it. |

Pass `--no-upload` to build and package without touching the release.
