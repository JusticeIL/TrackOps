import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// Heebo is bundled from npm and served by our own nginx: Hebrew and Latin subsets, three weights,
// font-display: swap. No request leaves for a font CDN.
import '@fontsource/heebo/hebrew-400.css'
import '@fontsource/heebo/hebrew-500.css'
import '@fontsource/heebo/hebrew-700.css'
import '@fontsource/heebo/latin-400.css'
import '@fontsource/heebo/latin-500.css'
import '@fontsource/heebo/latin-700.css'
import './shared/styles/tokens.css'
import './shared/styles/base.css'
import { App } from './app/App'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
