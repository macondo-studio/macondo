/**
 * Macondo - Obsidian & Supabase Developer Aesthetic
 * Dynamic Version & Date Utilities
 */

const pageLang = document.documentElement.lang || 'en';

let remoteVersion = null;
let remotePublishedAt = "2026-09-10T00:00:00Z";

async function fetchVersion() {
  try {
    const res = await fetch('version.json');
    if (res.ok) {
      const data = await res.json();
      if (data) {
        if (data.version) remoteVersion = data.version;
        if (data.publishedAt) remotePublishedAt = data.publishedAt;
        updateVersionBadge();
        updateReleaseDates();
      }
    }
  } catch (e) {
    // Silent fallback
  }
}

function updateVersionBadge() {
  if (!remoteVersion) return;
  const pillEl = document.querySelector('.hero-pill-text');
  if (pillEl) {
    pillEl.innerHTML = pageLang === 'it'
      ? `Macondo v${remoteVersion} per Windows • Gratuito`
      : `Macondo v${remoteVersion} for Windows • Free`;
  }
}

function updateReleaseDates() {
  if (!remotePublishedAt) return;
  const d = new Date(remotePublishedAt);
  if (isNaN(d.getTime())) return;

  const formattedDate = pageLang === 'it'
    ? d.toLocaleDateString('it-IT', { day: 'numeric', month: 'long', year: 'numeric' })
    : d.toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' });

  const label = pageLang === 'it'
    ? `Ultimo aggiornamento: ${formattedDate}`
    : `Last updated ${formattedDate}`;

  document.querySelectorAll('.release-date-text').forEach(el => {
    el.textContent = label;
  });
}

document.addEventListener('DOMContentLoaded', () => {
  // Set current year dynamically
  const yearEl = document.getElementById('current-year');
  if (yearEl) {
    yearEl.textContent = new Date().getFullYear();
  }

  fetchVersion();
});
