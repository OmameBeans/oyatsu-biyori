// おやつ日和 画面モック 共通スクリプト
// <body data-nav="menus|products|settings"> の画面に、アプリの枠（SC-02）を差し込む。
(function () {
  const nav = document.body.dataset.nav;

  if (nav) {
    const items = [
      { key: "menus", href: "menus.html", icon: "calendar_month", label: "献立一覧", badge: "" },
      { key: "products", href: "products.html", icon: "inventory_2", label: "商品一覧", badge: '<span class="badge badge-warn">7</span>' },
      { key: "settings", href: "settings.html", icon: "settings", label: "設定", badge: "" },
    ];
    const navHtml = items
      .map(
        (i) => `<a class="nav-item${i.key === nav ? " active" : ""}" href="${i.href}">
          <span class="material-symbols-outlined">${i.icon}</span>${i.label}${i.badge}</a>`
      )
      .join("");

    document.body.insertAdjacentHTML(
      "afterbegin",
      `<header class="appbar">
        <button class="icon-btn menu-btn" aria-label="メニューを開く" data-drawer-toggle>
          <span class="material-symbols-outlined">menu</span>
        </button>
        <a class="brand" href="menus.html" style="color:#fff;text-decoration:none">
          <span class="brand-mark"><span class="material-symbols-outlined icon-fill">cookie</span></span>
          おやつ日和
        </a>
        <span class="spacer"></span>
        <div class="user">
          <span class="name">ひまわり学童クラブ</span>
          <span class="avatar"><span class="material-symbols-outlined">person</span></span>
        </div>
      </header>
      <nav class="drawer" aria-label="メニュー">
        ${navHtml}
        <div class="grow"></div>
        <div class="drawer-note">
          <b>アレルゲンの最終確認</b>は、必ずパッケージの原材料表示で行ってください。
        </div>
        <hr class="divider">
        <a class="nav-item" href="login.html"><span class="material-symbols-outlined">logout</span>ログアウト</a>
      </nav>
      <div class="scrim" data-drawer-toggle></div>`
    );
  }

  // モックの一覧へ戻る案内（index.html 自身とスマホ幅では出さない）
  if (!document.body.dataset.noBanner) {
    document.body.insertAdjacentHTML(
      "beforeend",
      `<div class="mock-banner"><span class="material-symbols-outlined" style="font-size:16px">draw</span>モック ・ <a href="index.html">画面一覧へ</a></div>`
    );
  }

  document.addEventListener("click", (e) => {
    if (e.target.closest("[data-drawer-toggle]")) {
      document.body.classList.toggle("drawer-open");
    }
    // タブ：<div class="tabs" data-tabs> 内の .tab[data-tab] と .tab-panel[data-panel] を切り替える
    const tab = e.target.closest(".tab[data-tab]");
    if (tab) {
      const group = tab.closest("[data-tabs]");
      const scope = document.querySelector(group.dataset.tabs) || document;
      group.querySelectorAll(".tab").forEach((t) => t.classList.toggle("active", t === tab));
      scope.querySelectorAll(".tab-panel").forEach((p) => (p.hidden = p.dataset.panel !== tab.dataset.tab));
    }
  });

  // ?tab=xxx で初期タブを選ぶ（スクリーンショット確認用）
  const initial = new URLSearchParams(location.search).get("tab");
  if (initial) {
    const t = document.querySelector(`.tab[data-tab="${initial}"]`);
    if (t) t.click();
  }
})();
