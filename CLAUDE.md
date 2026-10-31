# Project instructions

## Pushing to GitHub

### Cloud sessions (claude.ai/code)

- Use the environment's own GitHub access. Do not look for or ask for `.artemgontar-gh-token`; it does not exist there.
- Push with `git push -u origin <branch>`. If git cannot push, use the GitHub connector tools (`mcp__github__*`, for example `push_files`).
- Push `main` and the working branch together when the user asks to push.

### Local machine only

- The default Git login on the user's own computer is a work account that cannot write to this repository.
- There, push with the token in `.artemgontar-gh-token` (repository root, ignored by Git).
- Never print the token, never write it to a Git config, remote URL, or log, and never commit it.
- Pass it for one command only:

```bash
TOKEN=$(tr -d '\r\n' < .artemgontar-gh-token)
AUTH=$(printf 'x-access-token:%s' "$TOKEN" | base64 -w0)
git -c credential.helper= -c "http.https://github.com/.extraheader=AUTHORIZATION: basic $AUTH" push origin <branch>
```

- Push `main` and the working branch together when the user asks to push.
- If the file is missing on a local machine, tell the user. Do not look for other credentials.

## Running the app

- Always run the app without sound.
- Before the page loads, set `localStorage['jericho.muted'] = '1'` in the browser.
  Or press the speaker button on the title screen or the map.
- The sound setting is saved per origin, so set it again after you open a new origin.
- When you finish, shut the app down.
  Stop the dev server (port 5173) and close the preview tab.
  Do not leave a server or browser tab running.
