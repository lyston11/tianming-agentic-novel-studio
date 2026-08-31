import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = join(__dirname, '../src/components/Settings');
const aiConfig = readFileSync(join(root, 'AIConfigTab.tsx'), 'utf8');
const creative = readFileSync(join(root, 'CreativeTab.tsx'), 'utf8');
const ui = readFileSync(join(root, 'UITab.tsx'), 'utf8');
const settingsSelect = readFileSync(join(root, 'SettingsSelect.tsx'), 'utf8');
const styles = readFileSync(join(__dirname, '../src/styles/settings.css'), 'utf8');

for (const [name, source] of [
  ['AIConfigTab', aiConfig],
  ['CreativeTab', creative],
  ['UITab', ui],
]) {
  assert.doesNotMatch(
    source,
    /<select\b/,
    `${name} should not use native select menus because browser option popups ignore the dark theme`,
  );

  assert.match(
    source,
    /SettingsSelect/,
    `${name} should use the themed SettingsSelect control`,
  );
}

assert.match(
  settingsSelect,
  /role="listbox"/,
  'SettingsSelect should render an app-owned listbox instead of a native browser popup',
);

assert.match(
  settingsSelect,
  /aria-selected=\{selected\}/,
  'SettingsSelect should expose selected option state for accessibility',
);

assert.match(
  styles,
  /\.settings-select-menu/,
  'settings stylesheet should include the themed dropdown menu surface',
);

assert.match(
  styles,
  /\.settings-select-menu\s*\{[\s\S]*position:\s*fixed/,
  'settings dropdown should escape collapse panel clipping with a fixed app-owned menu',
);
