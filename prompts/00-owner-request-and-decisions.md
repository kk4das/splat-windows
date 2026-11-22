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
- "american legion hall - use mobile - about 6' - my QTH on second story chimney - about 30'  - repeater call sign is K4HTS" (D7).
- "correction - repeater is k4hta, vertical polarization is correct" (call sign K4HTA; D7 complete).
- "Yes, start Stage 1" (D5 → recommended region sizes).
- Terrain download: "Yes, download both (Recommended)" (AWS skadi 1-arc-second + viewfinderpanoramas J18.zip).
- "stage 1 approved. use underscore for filenames, userprofile is good for settings" (D3 → _, D4 → %USERPROFILE%).
- "Yes, start Stage 2".
- "Approve, keep CR-LF" (Stage 2 approved; D11 → CR-LF).
- "Yes, start Stage 3".
- "Approve Stage 3 and D12" (Stage 3 approved; last-digit tolerance for .gp/.ano ratified).
- "Yes, start Stage 4" (D8 → MSYS2 gnuplot). D9: "ppm2png helper (Recommended)". USGS postdownload: "Defer it (Recommended)".
- "Accept D13" (graph PNGs pass on same size plus .gp data passing D12; pixel % for information).
- (Stage 4 README run) "that worked" (coverage map in Google Earth Pro). "yes, add kmz to ppm2png and the readme". Project moved to D:\ClaudeAI\projects\SPLATWIN at owner request.
- "Checkpoint and save now. Next step is to plan a GUI front end to eliminate the need for command line and make it a modern windows program. It needs multiple options for locating stations - a call sign lookup on QRZ.com is the most common. hams are familiar with maidenhead square designation so we should provide that option as well. We should view the coverage map in the application itself and provide a link out to google earth pro if possible."
- GUI G3: "Yes, I subscribe" (QRZ XML Logbook Data subscription).
- GUI decisions: G1 "C#/.NET 10 WPF (Recommended)", G2 "Streets + terrain (Recommended)", G4 "App draws graphs (Recommended)", G5/D10 "Club members too (Recommended)", G6 "GUI first (Recommended)", G7 "GPLv2 (Recommended)", G8 "SPLAT! for Windows".
- D6: "we can host on my github - kk4das - I'll make generally available to hams once we have club members try it out". "start G0" (approves the .NET 10 SDK install).
