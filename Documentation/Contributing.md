### Contributing

### Coding Guidelines
Follow the [Code Standards](CodeStandards.md). Read the [Architecture](Architecture.md) overview before making structural changes.

### Build & Run Prerequisites
Follow the [README](../README.md).

### AI
See [`AGENTS.md`](../AGENTS.md) for the standards AI agents follow in this project. AI is used with GREAT care and scrutiny. All AI-generated code must be reviewed and approved by a human before being merged, and any AI-generated code must follow the same standards as human-written code.

### Merging and Approval
Make a fork with your desired changes, then open a pull request to merge it back into the main repo. A Toybox team member will review, offer comments, and approve once all comments are addressed.

Before opening a PR: build clean (`dotnet build Toybox.Studio.slnx` — warnings are errors), run the app, exercise your change, and confirm `~/.toybox/Logs/TbxStudio.log` is free of new warnings or errors. Verify UI/rendering changes visually.
