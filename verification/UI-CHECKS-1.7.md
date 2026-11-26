# Internet UI verification — 1.7

Used an isolated synthetic Data profile, no real API key and no loaded model for desktop inspection.

- Internet… was visible in the resident sidebar. Opened its real WPF dialog and inspected the mode dropdown, masked key field, no-key explanation, remove-key checkbox, outgoing-query disclosure and Save control.
- The initial visual check exposed a white-window/white-text inheritance problem. Added explicit app background/foreground/font settings, rebuilt, reopened and verified readable dark styling with Save fully visible.
- Expanded the mode dropdown and observed Off, Ask first and Automatic. Closed it without changing the mode or entering any credential. Native privacy settings were not changed through automation. Saving/preservation, encryption and mode decisions are exercised by isolated application tests.
- Closed the settings/main windows and released the exact native capture helper created for this inspection. No test app, model or avatar was left running.

32 web checks include clickable-link construction, live public HTTPS retrieval, a real local-model URL answer, fixture-based search schema/auth handling, Off/declined-request behavior, credential encryption and source/usage persistence. Existing functionality adds 190 checks, for 222 total. The final styling revision was rechecked with the web suite and native UI; the other regression suites cover the same feature logic before that visual-only correction.

No live paid-provider search was performed: the user must configure a Brave Search API key. No login, JavaScript browser interaction or automatic background browsing is implemented. Source correctness and automatic lookup judgment remain model-dependent.
