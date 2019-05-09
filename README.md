# GitHubReleaseNotes

Generates release notes for a milestone in a GitHub repo based on issues associated with the milestone.

### Conventions

- All closed issues/PR's for a milestone will be included.
- Issues/PR's with a label `Type: Bug` will be included in a `Bugs` section
- Issues/PR's not labeled as `Type: Bug` will be included in a `Features/Improvements` section
- The version is picked up from the build number (GFV) and that info is used to find the milestone.
- Release notes are generated as markdown.
- The `c` flag, along with a file path argument, `f`, will generate a file rather than a release in GitHub
