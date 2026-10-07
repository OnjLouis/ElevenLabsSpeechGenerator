# ElevenLabs Speech Generator

Accessible native Windows and Mac applications for speech and audio workflows through your own ElevenLabs account. By Andre Louis.

## Downloads

Choose your platform on the [releases page](https://github.com/OnjLouis/ElevenLabsSpeechGenerator/releases/latest):

- Windows: extract the whole ZIP into a writable folder and run `ElevenLabsSpeechGenerator.exe`. Keep the accompanying files beside it. Requires Windows 10 or later and .NET Framework 4.8.
- Apple Silicon Mac: choose the Mac ZIP for M-series computers, extract the app and move it to Applications.
- Intel Mac: choose the separate Mac Intel ZIP, extract the app and move it to Applications.

Both Mac downloads require macOS 14 or later and are signed and notarized. The apps include signed update installation and configurable startup update checks.

## Getting Started

You need an ElevenLabs account, sufficient credits and an [API key](https://elevenlabs.io/app/settings/api-keys) with access to the features you intend to use. Open Preferences or Settings, save your key, choose the default output folder and use Test API Key. Enable User read access if you want the credit balance display. The manuals explain the feature-specific permissions.

Choose a mode, enter text or select an input file, then review the confirmation before generating. Selecting a file does not upload it. Generated recordings are saved automatically; personal settings, drafts and keys are not part of the application download.

## Features

- Text to speech and multi-voice dialogue, with voice settings and an insertable speech-tag picker.
- Transcription with text and subtitles, and alignment of an existing transcript to its recording.
- Voice conversion and background-noise isolation.
- Voice design and remix previews, plus voice-library and cloning tools.
- Automatic dubbing from local audio/video or a public HTTPS source, with saved jobs and safe resume.
- Pronunciation dictionaries and generation history.
- Audio-device selection, optional sequential playback and reusable projects.
- A readable credit balance, native keyboard controls and F1 help for the focused control.

Voice cloning and other account-changing actions require the appropriate permissions and explicit confirmation. Use only recordings and voices you have the necessary rights and consent to use. Automatic dubbing spends credits when a project is accepted; stopping local monitoring does not cancel server-side work or its charge. Resume retrieves the existing project rather than creating another paid job.

## Manuals and Privacy

The [Windows manual](Manual.html) and [Mac manual](Mac/Manual.html) cover setup, keyboard shortcuts, output files, recovery and updates. Windows keeps portable settings beside the app and protects API keys for the current Windows account and computer. Mac stores the key in Keychain and uses normal application-support storage. Do not include personal settings, keys, drafts or recordings when sharing the app.

Confirmed generation sends the relevant text or recording to ElevenLabs. Available models, formats, charges and voice slots depend on your subscription.

For music and sound effects, use the [companion Music and Sound FX Generator](https://github.com/OnjLouis/ElevenLabsMusicGenerator).

[Software catalogue](https://onj.me/software) | [Contact](https://onj.me/contact) | [Donate](https://onj.me/donate)
