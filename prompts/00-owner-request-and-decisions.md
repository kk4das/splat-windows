# Owner request (2026-11-21), as given in chat

- "The purpose of this session is to create a windows version [of] the SPLAT terrestrial RF Path and
  Terrain Analysis Tool."
- "Create a runbook for the project — see the Flex Radio monitoring system runbook as a reference for
  how I like to work."
- "After creating the runbook, examine the SPLAT project, the github for the Linux version is here:
  https://github.com/jmcmellen/splat and make a plan for a Windows port."
- "The build can use whatever tools are already installed on this machine or you can recommend tools
  [to] use."

## Decisions
- "vscode is installed on this system. is the MSYS2UCRT64 tool chain compatible? That's not a
  requirement, just a question."
- "The linux version can be built on a new wsl distro" (D2).
- "Approve, start Stage 0" (D1 → MSYS2 UCRT64).
