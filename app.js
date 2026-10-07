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
  document.querySelectorAll('.statusbar-title').forEach(el => {
    el.textContent = `Macondo v${remoteVersion}`;
  });
  document.querySelectorAll('.nav-version-tag').forEach(el => {
    el.textContent = `v${remoteVersion}`;
  });
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

function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

function formatReleaseDate(isoString, isIt) {
  if (!isoString) return '';
  const d = new Date(isoString);
  if (isNaN(d.getTime())) return '';
  return isIt
    ? d.toLocaleDateString('it-IT', { day: 'numeric', month: 'long', year: 'numeric' })
    : d.toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' });
}

function formatInline(text) {
  if (!text) return '';
  let str = escapeHtml(text);
  // Bold
  str = str.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
  // Italic
  str = str.replace(/\*(.*?)\*/g, '<em>$1</em>');
  // Inline Code
  str = str.replace(/`([^`]+)`/g, '<code>$1</code>');
  // Links
  str = str.replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');
  return str;
}

function renderMarkdown(md) {
  if (!md) return '';

  const lines = md.split(/\r?\n/);
  const out = [];
  const listStack = [];

  function closeAllLists() {
    while (listStack.length > 0) {
      listStack.pop();
      out.push('</li></ul>');
    }
  }

  let paragraphLines = [];

  function flushParagraph() {
    if (paragraphLines.length > 0) {
      out.push('<p>' + paragraphLines.join('<br>') + '</p>');
      paragraphLines = [];
    }
  }

  for (let i = 0; i < lines.length; i++) {
    const rawLine = lines[i];
    const trimmed = rawLine.trim();

    if (!trimmed) {
      flushParagraph();
      closeAllLists();
      continue;
    }

    // Headings
    const h4Match = trimmed.match(/^####\s+(.*)$/);
    const h3Match = trimmed.match(/^###\s+(.*)$/);
    const h2Match = trimmed.match(/^##\s+(.*)$/);
    const h1Match = trimmed.match(/^#\s+(.*)$/);

    if (h1Match || h2Match || h3Match || h4Match) {
      flushParagraph();
      closeAllLists();
      if (h1Match) out.push(`<h2 class="release-mainheading">${formatInline(h1Match[1])}</h2>`);
      else if (h2Match) out.push(`<h3 class="release-heading">${formatInline(h2Match[1])}</h3>`);
      else if (h3Match) out.push(`<h4 class="release-subheading">${formatInline(h3Match[1])}</h4>`);
      else if (h4Match) out.push(`<h5 class="release-subheading">${formatInline(h4Match[1])}</h5>`);
      continue;
    }

    // List item (match optional indent, - or *, space, content)
    const listMatch = rawLine.match(/^(\s*)[-*]\s+(.*)$/);
    if (listMatch) {
      flushParagraph();
      const indentStr = listMatch[1];
      const indent = indentStr.replace(/\t/g, '    ').length;
      const content = formatInline(listMatch[2]);

      if (listStack.length === 0) {
        listStack.push(indent);
        out.push(`<ul><li>${content}`);
      } else {
        const topIndent = listStack[listStack.length - 1];
        if (indent > topIndent) {
          listStack.push(indent);
          out.push(`<ul><li>${content}`);
        } else if (indent === topIndent) {
          out.push(`</li><li>${content}`);
        } else {
          // Unindent back to appropriate level
          while (listStack.length > 0 && listStack[listStack.length - 1] > indent) {
            listStack.pop();
            out.push('</li></ul>');
          }
          if (listStack.length === 0 || listStack[listStack.length - 1] < indent) {
            listStack.push(indent);
            out.push(`<ul><li>${content}`);
          } else {
            out.push(`</li><li>${content}`);
          }
        }
      }
      continue;
    }

    // Regular text / paragraph line
    if (listStack.length > 0) {
      const lineIndent = (rawLine.match(/^(\s*)/)[1] || '').replace(/\t/g, '    ').length;
      if (lineIndent > listStack[listStack.length - 1]) {
        out.push('<br>' + formatInline(trimmed));
        continue;
      } else {
        closeAllLists();
      }
    }

    paragraphLines.push(formatInline(trimmed));
  }

  flushParagraph();
  closeAllLists();

  return out.join('');
}

async function loadReleases() {
  const container = document.getElementById('releases-container');
  if (!container) return;

  const isIt = pageLang === 'it';
  const CACHE_KEY = 'macondo_gh_releases';
  const CACHE_TIME_KEY = 'macondo_gh_releases_time';
  const TTL = 15 * 60 * 1000; // 15 minutes cache

  let releases = null;

  try {
    const cachedTime = localStorage.getItem(CACHE_TIME_KEY);
    const cachedData = localStorage.getItem(CACHE_KEY);
    if (cachedTime && cachedData && (Date.now() - parseInt(cachedTime, 10)) < TTL) {
      releases = JSON.parse(cachedData);
    }
  } catch (_) {}

  if (!releases) {
    try {
      const res = await fetch('https://api.github.com/repos/macondo-studio/macondo/releases');
      if (res.ok) {
        releases = await res.json();
        try {
          localStorage.setItem(CACHE_KEY, JSON.stringify(releases));
          localStorage.setItem(CACHE_TIME_KEY, Date.now().toString());
        } catch (_) {}
      }
    } catch (_) {
      // Fallback handled below
    }
  }

  if (releases && Array.isArray(releases) && releases.length > 0) {
    renderReleases(container, releases, isIt);
  } else {
    await renderReleasesFallback(container, isIt);
  }
}

function renderReleases(container, releases, isIt) {
  container.innerHTML = '';
  releases.forEach((rel, index) => {
    const isLatest = index === 0 && !rel.prerelease;
    const tagName = rel.tag_name || (rel.name ? `v${rel.name}` : 'Release');
    const formattedDate = formatReleaseDate(rel.published_at, isIt);
    const bodyHtml = renderMarkdown(rel.body || (isIt ? 'Nessuna descrizione fornita per questa release.' : 'No description provided for this release.'));
    
    let setupAsset = null;
    let portableAsset = null;

    if (rel.assets && rel.assets.length > 0) {
      setupAsset = rel.assets.find(a => a.name && /setup/i.test(a.name) && a.name.endsWith('.exe'));
      portableAsset = rel.assets.find(a => a.name && !/setup/i.test(a.name) && a.name.endsWith('.exe'));
      if (!setupAsset && !portableAsset) {
        portableAsset = rel.assets.find(a => a.name && a.name.endsWith('.exe')) || rel.assets[0];
      }
    }

    let actionsHtml = '';
    if (setupAsset) {
      const sizeStr = setupAsset.size ? ` (${(setupAsset.size / (1024 * 1024)).toFixed(1)} MB)` : '';
      actionsHtml += `
        <a href="${escapeHtml(setupAsset.browser_download_url || rel.html_url)}" class="btn btn-sm btn-primary">
          ${isIt ? 'Scarica Installer' : 'Download Installer'}${escapeHtml(sizeStr)}
        </a>
      `;
    }
    if (portableAsset) {
      const sizeStr = portableAsset.size ? ` (${(portableAsset.size / (1024 * 1024)).toFixed(1)} MB)` : '';
      actionsHtml += `
        <a href="${escapeHtml(portableAsset.browser_download_url || rel.html_url)}" class="btn btn-sm ${setupAsset ? 'btn-secondary' : 'btn-primary'}">
          ${isIt ? 'Versione Portable' : 'Portable Edition'}${escapeHtml(sizeStr)}
        </a>
      `;
    }
    if (!setupAsset && !portableAsset) {
      actionsHtml += `
        <a href="${escapeHtml(rel.html_url)}" class="btn btn-sm btn-primary">
          ${isIt ? 'Scarica Release' : 'Download Release'}
        </a>
      `;
    }
    actionsHtml += `
      <a href="${escapeHtml(rel.html_url)}" target="_blank" rel="noopener noreferrer" class="btn btn-sm btn-secondary">
        GitHub &rarr;
      </a>
    `;

    const card = document.createElement('div');
    card.className = 'release-card';
    card.innerHTML = `
      <div class="release-card-header">
        <div class="release-title-group">
          <span class="release-tag">${escapeHtml(tagName)}</span>
          ${isLatest ? `<span class="release-badge-latest">${isIt ? 'Ultima Release' : 'Latest'}</span>` : ''}
          ${rel.prerelease ? `<span class="release-badge-prerelease">${isIt ? 'Pre-release' : 'Pre-release'}</span>` : ''}
          ${formattedDate ? `<span class="release-date">${escapeHtml(formattedDate)}</span>` : ''}
        </div>
        <div class="release-actions">
          ${actionsHtml}
        </div>
      </div>
      <div class="release-body">
        ${bodyHtml}
      </div>
    `;
    container.appendChild(card);
  });
}

async function renderReleasesFallback(container, isIt) {
  let vData = null;
  try {
    const res = await fetch('version.json');
    if (res.ok) vData = await res.json();
  } catch (_) {}

  const version = vData?.version || '1.5.0';
  const dateStr = vData?.publishedAt ? formatReleaseDate(vData.publishedAt, isIt) : '';
  const note = vData?.releaseNotes ? (isIt ? vData.releaseNotes.it : vData.releaseNotes.en) : '';
  const dlUrl = vData?.downloadUrl || 'https://github.com/macondo-studio/macondo/releases/latest/download/Macondo-Setup.exe';

  container.innerHTML = `
    <div class="release-card">
      <div class="release-card-header">
        <div class="release-title-group">
          <span class="release-tag">v${escapeHtml(version)}</span>
          <span class="release-badge-latest">${isIt ? 'Ultima Release' : 'Latest Release'}</span>
          ${dateStr ? `<span class="release-date">${escapeHtml(dateStr)}</span>` : ''}
        </div>
        <div class="release-actions">
          <a href="${escapeHtml(dlUrl)}" class="btn btn-sm btn-primary">
            ${isIt ? 'Scarica Installer' : 'Download Installer'}
          </a>
          <a href="https://github.com/macondo-studio/macondo/releases/latest/download/Macondo.exe" class="btn btn-sm btn-secondary">
            ${isIt ? 'Portable' : 'Portable'}
          </a>
          <a href="https://github.com/macondo-studio/macondo/releases" target="_blank" rel="noopener noreferrer" class="btn btn-sm btn-secondary">
            GitHub &rarr;
          </a>
        </div>
      </div>
      <div class="release-body">
        <p>${escapeHtml(note)}</p>
      </div>
    </div>
  `;
}

function initLanguageSwitcher() {
  document.querySelectorAll('.lang-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      const href = btn.getAttribute('href') || '';
      try {
        if (href.indexOf('it.html') !== -1 || href.indexOf('privacy-it.html') !== -1 || href.indexOf('releases-it.html') !== -1) {
          localStorage.setItem('macondo_lang', 'it');
        } else if (href.indexOf('index.html') !== -1 || href.indexOf('privacy.html') !== -1 || href.indexOf('releases.html') !== -1) {
          localStorage.setItem('macondo_lang', 'en');
        }
      } catch (_) {}
    });
  });
}

function initAnalysesTabs() {
  const tabButtons = document.querySelectorAll('.analysis-tab-btn');
  const panes = document.querySelectorAll('.analysis-pane');
  if (!tabButtons.length || !panes.length) return;

  tabButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      const targetId = btn.getAttribute('data-tab');
      if (!targetId) return;

      tabButtons.forEach(b => {
        const isActive = b === btn;
        b.classList.toggle('active', isActive);
        b.setAttribute('aria-selected', isActive ? 'true' : 'false');
      });

      panes.forEach(pane => {
        if (pane.id === targetId) {
          pane.hidden = false;
          // Trigger reflow to restart animation smoothly
          void pane.offsetWidth;
          pane.classList.add('active');
        } else {
          pane.classList.remove('active');
          pane.hidden = true;
        }
      });
    });
  });
}

function initSpecsCarousel() {
  const track = document.getElementById('specsTrack');
  const dotsContainer = document.getElementById('specsDots');
  if (!track || !dotsContainer) return;

  const cards = Array.from(track.querySelectorAll('.specs-card'));
  if (!cards.length) return;

  let autoScrollTimer = null;
  const intervalMs = 3000;

  function renderDots() {
    dotsContainer.innerHTML = '';
    cards.forEach((_, i) => {
      const dot = document.createElement('button');
      dot.className = 'specs-dot' + (i === 0 ? ' active' : '');
      dot.setAttribute('aria-label', `Slide ${i + 1}`);
      dot.addEventListener('click', () => {
        stop();
        const cardWidth = track.clientWidth;
        track.scrollTo({ left: i * cardWidth, behavior: 'smooth' });
        updateActiveDot(i);
        start();
      });
      dotsContainer.appendChild(dot);
    });
  }

  function updateActiveDot(index) {
    const dots = dotsContainer.querySelectorAll('.specs-dot');
    dots.forEach((dot, idx) => {
      dot.classList.toggle('active', idx === index);
    });
  }

  function advance() {
    const cardWidth = track.clientWidth;
    const currentIdx = Math.round(track.scrollLeft / (cardWidth || 1));
    const nextIdx = (currentIdx + 1) % cards.length;

    track.scrollTo({ left: nextIdx * cardWidth, behavior: 'smooth' });
    updateActiveDot(nextIdx);
  }

  function start() {
    stop();
    autoScrollTimer = setInterval(advance, intervalMs);
  }

  function stop() {
    if (autoScrollTimer) {
      clearInterval(autoScrollTimer);
      autoScrollTimer = null;
    }
  }

  let scrollTimeout = null;
  track.addEventListener('scroll', () => {
    if (scrollTimeout) cancelAnimationFrame(scrollTimeout);
    scrollTimeout = requestAnimationFrame(() => {
      const cardWidth = track.clientWidth;
      const activeIdx = Math.min(
        cards.length - 1,
        Math.max(0, Math.round(track.scrollLeft / (cardWidth || 1)))
      );
      updateActiveDot(activeIdx);
    });
  }, { passive: true });

  renderDots();
  start();

  window.addEventListener('resize', () => {
    const cardWidth = track.clientWidth;
    const activeDot = dotsContainer.querySelector('.specs-dot.active');
    const activeIdx = activeDot ? Array.from(dotsContainer.children).indexOf(activeDot) : 0;
    track.scrollTo({ left: activeIdx * cardWidth, behavior: 'auto' });
  }, { passive: true });

  track.addEventListener('mouseenter', stop);
  track.addEventListener('mouseleave', start);
  track.addEventListener('touchstart', stop, { passive: true });
  track.addEventListener('touchend', start, { passive: true });
}

function initGuideScrollSpy() {
  const sidebar = document.querySelector('.guide-sidebar');
  const navLinks = document.querySelectorAll('.guide-nav-link');
  const sections = document.querySelectorAll('.guide-section, .guide-content > section');

  if (!sidebar || navLinks.length === 0 || sections.length === 0) return;

  const linkMap = new Map();
  navLinks.forEach(link => {
    const href = link.getAttribute('href');
    if (href && href.startsWith('#')) {
      linkMap.set(href.slice(1), link);
    }
  });

  let activeSectionId = null;
  let isManualClick = false;
  let manualClickTimeout = null;

  function setActive(id, scrollSidebar = true) {
    if (!id) return;
    activeSectionId = id;

    navLinks.forEach(link => link.classList.remove('active'));
    sections.forEach(sec => sec.classList.remove('active-section'));

    const targetLink = linkMap.get(id);
    if (targetLink) {
      targetLink.classList.add('active');
      if (scrollSidebar) {
        // Center the active link within the sidebar
        const linkOffsetTop = targetLink.offsetTop;
        const linkHeight = targetLink.offsetHeight;
        const sidebarHeight = sidebar.clientHeight;
        const targetScrollTop = linkOffsetTop - (sidebarHeight / 2) + (linkHeight / 2);
        sidebar.scrollTo({
          top: Math.max(0, targetScrollTop),
          behavior: 'smooth'
        });
      }
    }

    const targetSection = document.getElementById(id);
    if (targetSection) {
      targetSection.classList.add('active-section');
    }
  }

  // Handle sidebar navigation clicks
  navLinks.forEach(link => {
    link.addEventListener('click', (e) => {
      const href = link.getAttribute('href');
      if (href && href.startsWith('#')) {
        const targetId = href.slice(1);
        isManualClick = true;
        if (manualClickTimeout) clearTimeout(manualClickTimeout);
        setActive(targetId, true);
        manualClickTimeout = setTimeout(() => {
          isManualClick = false;
        }, 700);
      }
    });
  });

  function getActiveSectionId() {
    // 0. If scrolled near or at the bottom of the page, select the last section
    const scrollBottom = window.innerHeight + window.scrollY;
    const docHeight = document.documentElement.scrollHeight;
    if (scrollBottom >= docHeight - 120 && sections.length > 0) {
      return sections[sections.length - 1].id;
    }

    const thresholdY = 180; // 84px header + comfort reading margin

    // 1. Check which section spans the reading line
    for (let i = 0; i < sections.length; i++) {
      const sec = sections[i];
      const rect = sec.getBoundingClientRect();
      if (rect.top <= thresholdY && rect.bottom > thresholdY) {
        return sec.id;
      }
    }

    // 2. If above the very first section
    const firstRect = sections[0].getBoundingClientRect();
    if (firstRect.top > thresholdY) {
      return sections[0].id;
    }

    // 3. Closest section to threshold (for edge cases)
    let bestSec = sections[0];
    let minDiff = Infinity;
    sections.forEach(sec => {
      const rect = sec.getBoundingClientRect();
      const diff = Math.abs(rect.top - thresholdY);
      if (diff < minDiff) {
        minDiff = diff;
        bestSec = sec;
      }
    });

    return bestSec ? bestSec.id : null;
  }

  let isTicking = false;
  function onScroll() {
    if (isManualClick) return;
    if (!isTicking) {
      window.requestAnimationFrame(() => {
        const currentId = getActiveSectionId();
        if (currentId && currentId !== activeSectionId) {
          setActive(currentId, true);
        }
        isTicking = false;
      });
      isTicking = true;
    }
  }

  window.addEventListener('scroll', onScroll, { passive: true });

  // Initial load check with hash support
  if (window.location.hash) {
    const hashId = window.location.hash.slice(1);
    if (linkMap.has(hashId)) {
      isManualClick = true;
      setActive(hashId, true);
      setTimeout(() => {
        isManualClick = false;
      }, 1000);
    } else {
      onScroll();
    }
  } else {
    onScroll();
  }
}

document.addEventListener('DOMContentLoaded', () => {
  // Set current year dynamically
  const yearEl = document.getElementById('current-year');
  if (yearEl) {
    yearEl.textContent = new Date().getFullYear();
  }

  initLanguageSwitcher();
  initAnalysesTabs();
  initSpecsCarousel();
  initGuideScrollSpy();
  fetchVersion();
  loadReleases();
});


