# Mac worker note

This session did not start a worker, did not log in, and did not create a credential. It asked the cloud API which workers this run could see.

The result was no connected self-hosted workers, and `pools` was an empty list. A worker is visible only while `agent worker start` is running on that machine. No Mac was connected. No shared pool was listed.

The documented personal setup is My Machines. The worker opens an outbound HTTPS connection. The docs say no inbound port is required. The commands below are what the Cursor docs tell a person to run on their own Mac. They were not run from this VM.

```bash
curl https://cursor.com/install -fsS | bash
agent login
cd /path/to/repo
agent worker start --name "my-mac"
```

`agent worker start --worker-dir /path/to/repo` is the documented way to point the worker at a checkout. A personal API key is the documented substitute for browser login on a machine with no browser. Service account keys are documented as pool workers, not My Machines workers. This run had no pool.

`--computer-use` is optional and was not requested. The docs say the first Mac start with that flag installs a helper and needs Accessibility and Screen Recording for the signed-in desktop user.

After a worker is connected, the iOS command is still `sh spikes/ios/scripts/simulator.sh` on that Mac. Connecting a worker does not by itself compile or launch the app.

Sources read through search results this session.

- https://cursor.com/docs/cloud-agent/self-hosted/my-machines
- https://cursor.com/docs/cli/reference/parameters
- https://cursor.com/help/ai-features/self-hosted-machines
