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
- D7 sites (owner, via Google Maps links): club repeater (TX), KK4DAS home QTH (RX 1), American
  Legion Hall (RX 2). "frequency is 146.685 MHz, ERP of 93 watts, 240 feet above ground level, it
  uses about 100 feet of hard line, feed line loss should be minimal." Coordinates are recorded in
  the runbook (the home QTH is rounded there).
- "25-30 miles is right" (D7 coverage radius).
