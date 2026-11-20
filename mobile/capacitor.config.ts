import type { CapacitorConfig } from '@capacitor/cli';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

// The release number lives in the repository's VERSION file (see docs/VERSIONING.md).
const version = readFileSync(join(__dirname, '..', 'VERSION'), 'utf8').trim();

// The app is a shell around the live cloud app. When it moves to its own domain, change this and www/offline.html.
const cloudUrl = 'https://dxvjhwlc6echh.cloudfront.net';

const config: CapacitorConfig = {
  appId: 'com.fourcsystems.cmms',
  appName: '4C CMMS',
  webDir: 'www',
  server: {
    // The technician view's start page: kept on the phone by a service worker, so the app opens with no connection.
    // With a connection it goes straight on to the full app.
    url: `${cloudUrl}/tech/`,
    // Shown instead of a browser error when the cloud app can't be reached.
    errorPath: 'offline.html',
  },
  plugins: {
    // Android 15+ draws apps edge to edge; 'native' keeps the cloud app's header clear of the status bar.
    SystemBars: { insetsHandling: 'native' },
  },
  android: {
    // Lets the web app show "Android app x.y.z" and the server's logs tell app traffic apart.
    appendUserAgent: `4CCMMS-Android/${version}`,
  },
};

export default config;
