import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from '@/App'
import { applyTheme, readCachedTheme } from '@/lib/theme'
import 'streamdown/styles.css'
import '@/index.css'

// The saved theme must land on <html> before the first render, otherwise every
// reload falls back to the light default until the settings page re-applies it.
applyTheme(readCachedTheme())

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
