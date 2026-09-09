/**
 * Macondo - Apple & Insomnia.rest Minimalist UI
 * Multilingual Engine (IT / EN) & Clean Interactive Showcase
 */

const i18n = {
  it: {
    brand_tag: "100% Gratuito",
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
    hero_note: "Gratuito per sempre • Nessuna installazione richiesta • 100% locale",

    win_tab_graph: "Grafo Semantico",
    win_tab_blast: "Blast Radius Matrix",
    win_tab_llm: "AI Context Slice",

    // Section 1: Graph
    f1_tag: "Esplorazione Visuale",
    f1_title: "Comprendi ogni dipendenza a colpo d'occhio.",
    f1_desc: "Parsing AST completo di file .vbp, .frm, .bas, .cls e .ctl. Esplora gerarchie e chiamate con 4 algoritmi di layout ottimizzati per grandi progetti.",
    f1_p1: "Layout Radiale, Affinità, Cluster a Isole e simulazione fisica Force-Directed.",
    f1_p2: "Espansione dinamica di classi e moduli con visualizzazione granulare dei metodi.",
    f1_p3: "Prestazioni native a 60 FPS anche su codebase con oltre 10.000 simboli.",

    // Section 2: Blast Radius
    f2_tag: "Analisi d'Impatto",
    f2_title: "Modifica senza paura di rompere la produzione.",
    f2_desc: "La matrice Blast Radius traccia ricorsivamente ogni chiamante a monte. Riconosci prima di toccare il codice quali schermate UI o classi verranno impattate.",
    f2_p1: "Valutazione automatica del rischio di regressione da Basso a Critico.",
    f2_p2: "Griglia interattiva con ordinamento per impatto totale e profondità di chiamata.",
    f2_p3: "Salto immediato con 1-click dalla riga della griglia al nodo nel grafo.",

    // Section 3: AI Slicing
    f3_tag: "AI & Modernizzazione",
    f3_title: "Il ponte ideale tra codice legacy e moderni LLM.",
    f3_desc: "Estrai Repo Map e contesti semantici compatti in formato Markdown pronti per Claude, ChatGPT, Gemini o Copilot per guidare la riscrittura in C#/.NET.",
    f3_p1: "Subgrafi mirati per evitare il rumore e massimizzare la precisione del modello.",
    f3_p2: "Esportazione istantanea con 1 click negli appunti o su file .md.",
    f3_p3: "Ideale per refactoring architetturali o migrazioni modulari passo dopo passo.",

    // Minimal Grid 3
    g1_title: "100% Gratuito",
    g1_desc: "Nessun abbonamento, nessuna licenza a pagamento, nessun limite artificiale. Uno strumento creato per la community.",
    g2_title: "Privacy Assoluta",
    g2_desc: "Funziona al 100% in locale sul tuo computer. Nessun sorgente o metadato viene mai inviato all'esterno o sul cloud.",
    g3_title: "Cache & Velocità",
    g3_desc: "Cache persistente ultra-veloce su disco: apri progetti enterprise con centinaia di moduli in una frazione di secondo.",

    // Download CTA
    dl_title: "Inizia subito ad analizzare.",
    dl_sub: "Scarica Macondo per Windows. Estrai l'archivio ed esplora il tuo primo progetto VB6 in pochi secondi.",
    dl_btn: "Download Macondo (x64 ZIP)",
    dl_meta_1: "Compatibile con Windows 10 e Windows 11",
    dl_meta_2: "Standalone & Portable",
    dl_meta_3: "Controllo aggiornamenti integrato",

    // Modal
    modal_title: "Primi passi con Macondo",
    modal_s1: "Scarica il pacchetto ZIP sul tuo computer.",
    modal_s2: "Estrai la cartella dove preferisci (es. Desktop o C:\\Tools).",
    modal_s3: "Avvia <strong>Macondo.exe</strong>, clicca su <strong>File &rarr; Apri Cartella VB6...</strong> e seleziona il tuo progetto.",
    modal_btn: "Conferma Download (ZIP)",

    footer_copy: "Macondo • Analisi semantica & reverse engineering per Visual Basic 6.0.",
    footer_manifest: "version.json"
  },

  en: {
    brand_tag: "100% Free",
    nav_graph: "Graph",
    nav_blast: "Blast Radius",
    nav_ai: "AI Context",
    nav_specs: "Specifications",
    nav_download: "Download",

    hero_pill: "Macondo v1.0.0 for Windows • Free",
    hero_title_lead: "See the structure.",
    hero_title_dim: "Master the legacy.",
    hero_subtitle: "Deterministic semantic code graph and Blast Radius impact analyzer for Visual Basic 6.0. 100% offline, absolute privacy, engineered for AI-guided migration.",
    hero_btn_dl: "Download for Windows",
    hero_btn_explore: "Explore features",
    hero_note: "Free forever • No installation required • 100% local",

    win_tab_graph: "Semantic Graph",
    win_tab_blast: "Blast Radius Matrix",
    win_tab_llm: "AI Context Slice",

    // Section 1: Graph
    f1_tag: "Visual Exploration",
    f1_title: "Understand every dependency at a glance.",
    f1_desc: "Exhaustive AST parsing of .vbp, .frm, .bas, .cls, and .ctl files. Explore call hierarchies with 4 tailored layout algorithms.",
    f1_p1: "Radial, Affinity, Clustered Islands, and Force-Directed physics layouts.",
    f1_p2: "Dynamic member expansion for classes and modules with granular method mapping.",
    f1_p3: "Fluid 60 FPS rendering even across large enterprise codebases with 10,000+ symbols.",

    // Section 2: Blast Radius
    f2_tag: "Impact Analysis",
    f2_title: "Refactor without fear of breaking production.",
    f2_desc: "The Blast Radius matrix recursively traces all upstream callers. Know which UI forms or business entities will be touched before editing a single line.",
    f2_p1: "Automated regression risk ratings from Low to Critical.",
    f2_p2: "Interactive audit grid sorted by total impact and call depth.",
    f2_p3: "1-click instant navigation from audit rows to the exact graph node.",

    // Section 3: AI Slicing
    f3_tag: "AI & Modernization",
    f3_title: "The ideal bridge between legacy code and AI models.",
    f3_desc: "Extract compact, structured Markdown Repo Maps and semantic context slices ready for Claude, ChatGPT, Gemini, or Copilot to guide your C#/.NET rewrite.",
    f3_p1: "Scoped subgraphs that minimize token noise and maximize LLM accuracy.",
    f3_p2: "Instant 1-click clipboard or .md file export.",
    f3_p3: "Perfect for phased, modular migrations from VB6 to modern stacks.",

    // Minimal Grid 3
    g1_title: "100% Free",
    g1_desc: "No subscriptions, no paywalls, no artificial limitations. Built as an open engineering tool for developers.",
    g2_title: "Absolute Privacy",
    g2_desc: "Runs 100% locally on your machine. Zero source code or telemetry ever leaves your computer.",
    g3_title: "Cache & Native Speed",
    g3_desc: "Lightning-fast persistent disk cache: reopen complex enterprise projects with hundreds of files in milliseconds.",

    // Download CTA
    dl_title: "Start analyzing today.",
    dl_sub: "Download Macondo for Windows. Extract the archive and explore your first VB6 project in seconds.",
    dl_btn: "Download Macondo (x64 ZIP)",
    dl_meta_1: "Compatible with Windows 10 & Windows 11",
    dl_meta_2: "Standalone & Portable",
    dl_meta_3: "Integrated update check",

    // Modal
    modal_title: "Getting started with Macondo",
    modal_s1: "Download the ZIP package to your PC.",
    modal_s2: "Extract the folder anywhere you like (e.g. Desktop or C:\\Tools).",
    modal_s3: "Launch <strong>Macondo.exe</strong>, select <strong>File &rarr; Open VB6 Folder...</strong> and choose your project folder.",
    modal_btn: "Confirm Download (ZIP)",

    footer_copy: "Macondo • Semantic analyzer & reverse engineering tool for Visual Basic 6.0.",
    footer_manifest: "version.json"
  }
};

let currentLang = 'it';

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

  const langCodeEl = document.getElementById('current-lang-code');
  if (langCodeEl) {
    langCodeEl.textContent = lang.toUpperCase();
  }
}

// Window Tab Switcher
function initWindowTabs() {
  const tabs = document.querySelectorAll('.window-tab');
  const panes = document.querySelectorAll('.viewport-tab-content');

  tabs.forEach(tab => {
    tab.addEventListener('click', () => {
      const target = tab.getAttribute('data-tab');
      tabs.forEach(t => t.classList.remove('active'));
      panes.forEach(p => p.classList.remove('active'));

      tab.classList.add('active');
      const pane = document.getElementById(`tab-${target}`);
      if (pane) pane.classList.add('active');
    });
  });
}

// Clean Canvas Graph Visualizer (Minimalist, Apple/Insomnia Precision)
function initCanvasGraph() {
  const canvas = document.getElementById('insomnia-canvas');
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  let w = 0, h = 0;

  function resize() {
    w = canvas.parentElement.clientWidth || 800;
    h = canvas.parentElement.clientHeight || 380;
    canvas.width = w * window.devicePixelRatio;
    canvas.height = h * window.devicePixelRatio;
    ctx.scale(window.devicePixelRatio, window.devicePixelRatio);
  }
  resize();
  window.addEventListener('resize', resize);

  const nodes = [
    { id: 1, label: "frmMain.frm", type: "UI", x: 0.20, y: 0.35, color: "#ffffff" },
    { id: 2, label: "frmInvoice.frm", type: "UI", x: 0.22, y: 0.68, color: "#ffffff" },
    { id: 3, label: "modAccounting.bas", type: "Logic", x: 0.48, y: 0.32, color: "#86868b" },
    { id: 4, label: "CalculateTax()", type: "Focal", x: 0.52, y: 0.58, color: "#ff6633", focal: true },
    { id: 5, label: "clsCustomer.cls", type: "Logic", x: 0.50, y: 0.80, color: "#86868b" },
    { id: 6, label: "clsDatabase.cls", type: "Data", x: 0.80, y: 0.42, color: "#a1a1a6" },
    { id: 7, label: "modAudit.bas", type: "Data", x: 0.82, y: 0.70, color: "#a1a1a6" }
  ];

  const edges = [
    { from: 1, to: 3 },
    { from: 1, to: 4, highlight: true },
    { from: 2, to: 4, highlight: true },
    { from: 3, to: 4, highlight: true },
    { from: 5, to: 4, highlight: true },
    { from: 4, to: 6, highlight: true },
    { from: 4, to: 7, highlight: true }
  ];

  let t = 0;

  function render() {
    ctx.clearRect(0, 0, w, h);

    // Edges
    edges.forEach(e => {
      const src = nodes.find(n => n.id === e.from);
      const dst = nodes.find(n => n.id === e.to);
      const x1 = src.x * w;
      const y1 = src.y * h;
      const x2 = dst.x * w;
      const y2 = dst.y * h;

      ctx.beginPath();
      ctx.moveTo(x1, y1);
      ctx.lineTo(x2, y2);
      ctx.strokeStyle = e.highlight ? "rgba(255, 102, 51, 0.4)" : "rgba(255, 255, 255, 0.08)";
      ctx.lineWidth = e.highlight ? 1.5 : 1;
      ctx.stroke();

      if (e.highlight) {
        // Small directional pulse dot
        const progress = (t * 0.5 + (src.id * 0.2)) % 1;
        const px = x1 + (x2 - x1) * progress;
        const py = y1 + (y2 - y1) * progress;
        ctx.beginPath();
        ctx.arc(px, py, 2, 0, Math.PI * 2);
        ctx.fillStyle = "#ff6633";
        ctx.fill();
      }
    });

    // Nodes
    nodes.forEach(n => {
      const nx = n.x * w;
      const ny = n.y * h;

      if (n.focal) {
        ctx.beginPath();
        ctx.arc(nx, ny, 14, 0, Math.PI * 2);
        ctx.fillStyle = "rgba(255, 102, 51, 0.15)";
        ctx.fill();
      }

      ctx.beginPath();
      ctx.arc(nx, ny, n.focal ? 7 : 5, 0, Math.PI * 2);
      ctx.fillStyle = n.color;
      ctx.fill();

      // Label
      ctx.font = n.focal ? "600 12px -apple-system, sans-serif" : "400 11px -apple-system, sans-serif";
      ctx.fillStyle = n.focal ? "#ffffff" : "#a1a1a6";
      ctx.textAlign = "center";
      ctx.fillText(n.label, nx, ny + 18);
    });

    t += 0.015;
    requestAnimationFrame(render);
  }

  requestAnimationFrame(render);
}

// Modal Handlers
function initModal() {
  const modal = document.getElementById('download-modal');
  const openButtons = document.querySelectorAll('.open-modal');
  const closeButton = document.getElementById('close-modal');

  openButtons.forEach(btn => {
    btn.addEventListener('click', e => {
      e.preventDefault();
      if (modal) modal.classList.add('active');
    });
  });

  if (closeButton) {
    closeButton.addEventListener('click', () => {
      if (modal) modal.classList.remove('active');
    });
  }

  if (modal) {
    modal.addEventListener('click', e => {
      if (e.target === modal) modal.classList.remove('active');
    });
  }
}

document.addEventListener('DOMContentLoaded', () => {
  const savedLang = localStorage.getItem('macondo_lang') || 'it';
  setLanguage(savedLang);

  const langBtn = document.getElementById('lang-toggle');
  if (langBtn) {
    langBtn.addEventListener('click', () => {
      const nextLang = currentLang === 'it' ? 'en' : 'it';
      setLanguage(nextLang);
    });
  }

  initWindowTabs();
  initCanvasGraph();
  initModal();
});
