/**
 * Macondo - Obsidian & Supabase Developer Aesthetic
 * Multilingual Engine (IT / EN) & Interactive Showcase
 */

const i18n = {
  it: {
    nav_graph: "Grafo",
    nav_blast: "Blast Radius",
    nav_ai: "AI Context",
    nav_specs: "Specifiche",
    nav_download: "Scarica",

    hero_pill: "Macondo v1.0.0 per Windows • Gratuito",
    hero_title_lead: "Vedi la struttura.",
    hero_title_dim: "Domina il legacy.",
    hero_subtitle: "Analizzatore semantico e calcolo del Blast Radius per Visual Basic 6.0. 100% offline, privacy assoluta, pronto per la migrazione con modelli AI.",
    hero_btn_dl: "Scarica per Windows",
    hero_btn_explore: "Guarda le funzionalità",
    hero_note: "Gratuito • Nessuna installazione richiesta • 100% locale",

    // Feature 1: Graph
    f1_tag: "Esplorazione Visuale",
    f1_title: "Comprendi ogni dipendenza a colpo d'occhio.",
    f1_desc: "Parsing AST completo di file .vbp, .frm, .bas, .cls e .ctl. Esplora gerarchie e chiamate con 4 algoritmi di layout ottimizzati per grandi progetti.",
    f1_p1: "Layout Radiale, Affinità, Cluster a Isole e simulazione fisica Force-Directed.",
    f1_p2: "Espansione dinamica di classi e moduli con visualizzazione granulare dei metodi.",
    f1_p3: "Prestazioni native a 60 FPS anche su codebase con oltre 10.000 simboli.",
    f1_box_title: "Pipeline di Risoluzione AST:",
    f1_box_l1: "Form (.FRM) &rarr; Gestori Eventi UI",
    f1_box_l2: "Moduli Standard (.BAS) &rarr; Routine Globali",
    f1_box_l3: "Moduli di Classe (.CLS) &rarr; Istanze &amp; Membri",
    f1_box_total: "Totale: <strong>1.482 chiamate risolte</strong> (32ms cold parse)",

    // Feature 2: Blast Radius
    f2_tag: "Analisi d'Impatto",
    f2_title: "Modifica senza paura di rompere la produzione.",
    f2_desc: "La matrice Blast Radius traccia ricorsivamente ogni chiamante a monte. Riconosci prima di toccare il codice quali schermate UI o classi verranno impattate.",
    f2_p1: "Valutazione automatica del rischio di regressione da Basso a Critico.",
    f2_p2: "Griglia interattiva con ordinamento per impatto totale e profondità di chiamata.",
    f2_p3: "Salto immediato con 1-click dalla riga della griglia al nodo nel grafo.",
    f2_box_title: "Formula d'Impatto Ricorsiva:",
    f2_box_l1: "Profondità:",
    f2_box_l1_val: "6 livelli",
    f2_box_l2: "Chiamanti Diretti:",
    f2_box_l2_ind: "Indiretti:",
    f2_box_l3: "Form UI Impattate:",
    f2_box_l3_val: "11 form",
    f2_box_risk: "Punteggio di Rischio: <strong>CRITICO (94/100)</strong>",

    // Feature 3: AI Slicing
    f3_tag: "AI & Modernizzazione",
    f3_title: "Il ponte ideale tra codice legacy e moderni LLM.",
    f3_desc: "Estrai Repo Map e contesti semantici compatti in formato Markdown pronti per Claude, ChatGPT, Gemini o Copilot per guidare la riscrittura in C#/.NET.",
    f3_p1: "Subgrafi mirati per evitare il rumore e massimizzare la precisione del modello.",
    f3_p2: "Esportazione istantanea con 1 click negli appunti o su file .md.",
    f3_p3: "Ideale per refactoring architetturali o migrazioni modulari passo dopo passo.",
    f3_box_title: "Pronto per il tuo LLM preferito:",
    f3_box_sub: "1-click LLM Context Slice:",
    f3_box_copied: "&check; Markdown formattato copiato negli appunti",

    // Minimal Grid 3
    g1_title: "100% Gratuito",
    g1_desc: "Nessun abbonamento, nessuna licenza a pagamento, nessun limite artificiale. Uno strumento creato per la community.",
    g2_title: "Privacy Assoluta",
    g2_desc: "Funziona al 100% in locale sul tuo computer. Nessun sorgente o metadato viene mai inviato all'esterno o sul cloud.",
    g3_title: "Cache & Velocità",
    g3_desc: "Cache persistente ultra-veloce su disco: apri progetti enterprise con centinaia di moduli in una frazione di secondo.",

    // Download CTA
    dl_title: "Inizia subito ad analizzare.",
    dl_sub: "Scarica direttamente l'eseguibile Macondo per Windows. Nessuna installazione richiesta, avvio istantaneo.",
    dl_btn: "Scarica Macondo.exe (Windows x64)",
    dl_meta_1: "Compatibile con Windows 10 e Windows 11 (64-bit)",
    dl_meta_2: "Standalone & Portable (Nessuna installazione)",

    footer_license: "Licenza"
  },

  en: {
    nav_graph: "Graph",
    nav_blast: "Blast Radius",
    nav_ai: "AI Context",
    nav_specs: "Specifications",
    nav_download: "Download",

    hero_pill: "Macondo v1.0.0 for Windows • Free",
    hero_title_lead: "See the structure.",
    hero_title_dim: "Master the legacy.",
    hero_subtitle: "Deterministic semantic code graph and Blast Radius impact analyzer for Visual Basic 6.0. 100% offline, absolute privacy, engineered for AI-guided migration.",
    hero_btn_dl: "Download Macondo.exe",
    hero_btn_explore: "Explore features",
    hero_note: "Free • Single portable executable • 100% local",

    // Feature 1: Graph
    f1_tag: "Visual Exploration",
    f1_title: "Understand every dependency at a glance.",
    f1_desc: "Exhaustive AST parsing of .vbp, .frm, .bas, .cls, and .ctl files. Explore call hierarchies with 4 tailored layout algorithms.",
    f1_p1: "Radial, Affinity, Clustered Islands, and Force-Directed physics layouts.",
    f1_p2: "Dynamic member expansion for classes and modules with granular method mapping.",
    f1_p3: "Fluid 60 FPS rendering even across large enterprise codebases with 10,000+ symbols.",
    f1_box_title: "AST Resolution Pipeline:",
    f1_box_l1: "Forms (.FRM) &rarr; UI Event Handlers",
    f1_box_l2: "Standard Modules (.BAS) &rarr; Global Routines",
    f1_box_l3: "Class Modules (.CLS) &rarr; Instantiations &amp; Members",
    f1_box_total: "Total: <strong>1,482 calls resolved</strong> (32ms cold parse)",

    // Feature 2: Blast Radius
    f2_tag: "Impact Analysis",
    f2_title: "Refactor without fear of breaking production.",
    f2_desc: "The Blast Radius matrix recursively traces all upstream callers. Know which UI forms or business entities will be touched before editing a single line.",
    f2_p1: "Automated regression risk ratings from Low to Critical.",
    f2_p2: "Interactive audit grid sorted by total impact and call depth.",
    f2_p3: "1-click instant navigation from audit rows to the exact graph node.",
    f2_box_title: "Recursive Impact Formula:",
    f2_box_l1: "Depth:",
    f2_box_l1_val: "6 levels",
    f2_box_l2: "Direct Callers:",
    f2_box_l2_ind: "Indirect:",
    f2_box_l3: "Touched UI Forms:",
    f2_box_l3_val: "11 forms",
    f2_box_risk: "Regression Risk Score: <strong>CRITICAL (94/100)</strong>",

    // Feature 3: AI Slicing
    f3_tag: "AI & Modernization",
    f3_title: "The ideal bridge between legacy code and AI models.",
    f3_desc: "Extract compact, structured Markdown Repo Maps and semantic context slices ready for Claude, ChatGPT, Gemini, or Copilot to guide your C#/.NET rewrite.",
    f3_p1: "Scoped subgraphs that minimize token noise and maximize LLM accuracy.",
    f3_p2: "Instant 1-click clipboard or .md file export.",
    f3_p3: "Perfect for phased, modular migrations from VB6 to modern stacks.",
    f3_box_title: "Ready for your favorite LLM:",
    f3_box_sub: "1-click LLM Context Slice:",
    f3_box_copied: "&check; Formatted markdown copied to clipboard",

    // Minimal Grid 3
    g1_title: "100% Free",
    g1_desc: "No subscriptions, no paywalls, no artificial limitations. Built as an open engineering tool for developers.",
    g2_title: "Absolute Privacy",
    g2_desc: "Runs 100% locally on your machine. Zero source code or telemetry ever leaves your computer.",
    g3_title: "Cache & Native Speed",
    g3_desc: "Lightning-fast persistent disk cache: reopen complex enterprise projects with hundreds of files in milliseconds.",

    // Download CTA
    dl_title: "Start analyzing today.",
    dl_sub: "Download the standalone Macondo.exe for Windows. No installer or zip archive required.",
    dl_btn: "Download Macondo.exe (Windows x64)",
    dl_meta_1: "Compatible with Windows 10 & Windows 11 (64-bit)",
    dl_meta_2: "Single Portable Executable (No Install)",

    footer_license: "License"
  }
};

let currentLang = 'it';

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
  const pillEl = document.querySelector('[data-i18n="hero_pill"]');
  if (pillEl) {
    pillEl.innerHTML = currentLang === 'it'
      ? `Macondo v${remoteVersion} per Windows • Gratuito`
      : `Macondo v${remoteVersion} for Windows • Free`;
  }
}

function updateReleaseDates() {
  if (!remotePublishedAt) return;
  const d = new Date(remotePublishedAt);
  if (isNaN(d.getTime())) return;

  const formattedDate = currentLang === 'it'
    ? d.toLocaleDateString('it-IT', { day: 'numeric', month: 'long', year: 'numeric' })
    : d.toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' });

  const label = currentLang === 'it'
    ? `Ultimo aggiornamento: ${formattedDate}`
    : `Last updated ${formattedDate}`;

  document.querySelectorAll('.release-date-text').forEach(el => {
    el.textContent = label;
  });
}

function setLanguage(lang) {
  if (!i18n[lang]) return;
  currentLang = lang;
  localStorage.setItem('macondo_lang', lang);

  document.querySelectorAll('[data-i18n]').forEach(el => {
    const key = el.getAttribute('data-i18n');
    if (i18n[lang][key]) {
      el.innerHTML = i18n[lang][key];
    }
  });

  const itEl = document.getElementById('lang-it');
  const enEl = document.getElementById('lang-en');
  if (itEl && enEl) {
    if (lang === 'it') {
      itEl.classList.add('current');
      enEl.classList.remove('current');
    } else {
      itEl.classList.remove('current');
      enEl.classList.add('current');
    }
  }

  // Update real software screenshot dynamically
  const screenshotEl = document.getElementById('graph-screenshot');
  if (screenshotEl) {
    screenshotEl.src = lang === 'it' ? 'VistaGrafoIta.png' : 'VistaGrafoEng.png';
  }

  updateVersionBadge();
  updateReleaseDates();
}

document.addEventListener('DOMContentLoaded', () => {
  // Set current year dynamically
  const yearEl = document.getElementById('current-year');
  if (yearEl) {
    yearEl.textContent = new Date().getFullYear();
  }

  const savedLang = localStorage.getItem('macondo_lang') || 'it';
  setLanguage(savedLang);

  const langBtn = document.getElementById('lang-toggle');
  if (langBtn) {
    langBtn.addEventListener('click', () => {
      const nextLang = currentLang === 'it' ? 'en' : 'it';
      setLanguage(nextLang);
    });
  }

  fetchVersion();
});
