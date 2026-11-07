// Fluent 2 (Microsoft Design) web components bundle for VideoCreator.
// Build: npm run build  ->  src/VideoCreator.App/wwwroot/js/fluent.bundle.js
import { setTheme } from '@fluentui/web-components';
import { webLightTheme, webDarkTheme } from '@fluentui/tokens';

import '@fluentui/web-components/button/define.js';
import '@fluentui/web-components/anchor-button/define.js';
import '@fluentui/web-components/badge/define.js';
import '@fluentui/web-components/checkbox/define.js';
import '@fluentui/web-components/dialog/define.js';
import '@fluentui/web-components/dialog-body/define.js';
import '@fluentui/web-components/divider/define.js';
import '@fluentui/web-components/field/define.js';
import '@fluentui/web-components/label/define.js';
import '@fluentui/web-components/link/define.js';
import '@fluentui/web-components/message-bar/define.js';
import '@fluentui/web-components/progress-bar/define.js';
import '@fluentui/web-components/radio/define.js';
import '@fluentui/web-components/radio-group/define.js';
import '@fluentui/web-components/spinner/define.js';
import '@fluentui/web-components/switch/define.js';
import '@fluentui/web-components/tab/define.js';
import '@fluentui/web-components/tablist/define.js';
import '@fluentui/web-components/text/define.js';
import '@fluentui/web-components/text-input/define.js';
import '@fluentui/web-components/textarea/define.js';
import '@fluentui/web-components/tooltip/define.js';

// Follow the OS light/dark preference (Fluent provides both web themes).
const media = window.matchMedia('(prefers-color-scheme: dark)');
const apply = () => {
  setTheme(media.matches ? webDarkTheme : webLightTheme);
  document.documentElement.dataset.theme = media.matches ? 'dark' : 'light';
};
apply();
media.addEventListener('change', apply);
