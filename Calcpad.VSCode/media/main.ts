// Webview client for the Calcpad preview. jQuery is loaded as a global <script>
// before this bundle, so we reference it as `$`. Communicates with the extension
// host via the VS Code webview messaging API.
declare const $: any;
declare function acquireVsCodeApi(): {
  postMessage(msg: unknown): void;
  getState(): any;
  setState(state: any): void;
};

type Mode = 'interactive' | 'final';

const vscode = acquireVsCodeApi();
const root = document.getElementById('calcpad-root')!;

let mode: Mode = (vscode.getState()?.mode as Mode) || 'interactive';
let lastHtml = '<div class="calcpad-status">Rendering…</div>';
let imgBase = '';

function isNumeric(s: string): boolean {
  return /^-?\d+(?:\.\d+)?$/.test(s);
}

function getTargetId(el: any): string | undefined {
  const name = $(el).attr('name');
  if (name && name.length > 0) {
    return name;
  }
  return $(el).data('target');
}

function getValue(id: string, source: any): string {
  let value = '';
  let target = $('#' + id + ' input');
  if (target.get(0) == null) {
    target = $('#' + id + ' .eq u');
    target.each(function (this: any) {
      value += $(this).text() + ';';
    });
    $(source).prop('disabled', true);
  } else {
    target.each(function (this: any) {
      value += $(this).val() + ';';
    });
  }
  if (value.length > 1) {
    value = value.slice(0, -1);
  }
  return value;
}

// In calculated output the parser renders filled input fields as read-only
// <u class="input-N">value</u>. For interactive mode we turn them back into
// editable <input> elements so the user can change them and trigger a recalc.
function makeInputsEditable(): void {
  $('#calcpad-root u[class*="input-"]').each(function (this: HTMLElement) {
    const m = this.className.match(/input-\d+/);
    if (!m) {
      return;
    }
    const value = $(this).text();
    const size = Math.max(2, String(value).length);
    const input = $('<input type="text" name="Var">')
      .addClass(m[0])
      .attr('size', size)
      .val(value);
    $(this).replaceWith(input);
  });
}

function collectInputs(): { line: number; value: string }[] {
  return $("input[type='text'][name='Var']")
    .map(function (this: any) {
      const m = this.className.match(/input-(\d+)/);
      return { line: m ? parseInt(m[1], 10) : NaN, value: $(this).val() };
    })
    .get()
    .filter((iv: any) => !isNaN(iv.line));
}

function currentUnits(): string | undefined {
  const u = $('#Units');
  return u.length ? u.val() : undefined;
}

function sendRecalc(type: 'inputChange' | 'unitChange'): void {
  vscode.postMessage({ type, inputValues: collectInputs(), units: currentUnits() });
}

function rewriteImages(): void {
  if (!imgBase) {
    return;
  }
  $('#calcpad-root img').each(function (this: HTMLImageElement) {
    const raw = this.getAttribute('src');
    if (!raw) {
      return;
    }
    const src = raw.trim();
    if (/^(https?:|data:|vscode-webview:|vscode-resource:|blob:)/.test(src)) {
      return;
    }
    try {
      this.setAttribute('src', new URL(src, imgBase).toString());
    } catch {
      /* ignore malformed src */
    }
  });
}

// Replicates the worksheet template's $(document).ready() wiring (minus the
// WPF-only chrome.webview hooks) and adds the recalc round-trip hooks.
function initContent(): void {
  $('.dvcs:has(.block) > :first-child').html('&hairsp;');

  $('#Units')
    .off('change.cp')
    .on('change.cp', function (this: any) {
      $('.Units').text($(this).val());
      sendRecalc('unitChange');
    });

  $('.fold > :first-child')
    .off('click.cp')
    .on('click.cp', function (this: any) {
      const parent = $(this).parent();
      if (parent.hasClass('fold')) {
        parent.removeClass('fold').addClass('unfold');
      } else {
        parent.removeClass('unfold').addClass('fold');
      }
    });

  $('select').each(function (this: any) {
    if ($(this).prop('id') !== 'Units') {
      const id = getTargetId(this);
      if (id) {
        $(this).val(getValue(id, this));
      }
    }
  });

  $('select')
    .off('change.cpsel')
    .on('change.cpsel', function (this: any) {
      if ($(this).prop('id') === 'Units') {
        return;
      }
      const id = getTargetId(this);
      if (id) {
        const target = $('#' + id + ' input');
        const values = String($(this).val()).split(';');
        target.each(function (this: any, index: number) {
          $(this).val(values[index]);
        });
        sendRecalc('inputChange');
      }
    });

  $('.money').each(function (this: any) {
    $(this).text(Number($(this).text()).toFixed(2));
  });

  $("input[name='Var']")
    .off('change.cpinput')
    .on('change.cpinput', function (this: any) {
      const e = $(this);
      const s = e.val();
      if (isNumeric(s)) {
        e.css('color', '').removeAttr('title');
      } else {
        e.css('color', 'red').attr('title', 'Invalid number');
      }
      sendRecalc('inputChange');
    });

  rewriteImages();
}

function applyRender(): void {
  root.innerHTML = lastHtml;
  if (mode === 'interactive') {
    makeInputsEditable();
  }
  initContent();
}

function setMode(next: Mode): void {
  mode = next;
  vscode.setState({ mode });
  $('#cp-mode-interactive').toggleClass('cp-active', mode === 'interactive');
  $('#cp-mode-final').toggleClass('cp-active', mode === 'final');
  applyRender();
}

window.addEventListener('message', (event) => {
  const data = event.data;
  if (!data || typeof data.type !== 'string') {
    return;
  }
  if (data.type === 'update') {
    lastHtml = data.html;
    if (typeof data.imgBase === 'string') {
      imgBase = data.imgBase;
    }
    applyRender();
  } else if (data.type === 'error') {
    root.innerHTML = '<div class="calcpad-error"></div>';
    const div = root.querySelector('.calcpad-error');
    if (div) {
      div.textContent = data.message;
    }
  }
});

// Wire the mode toolbar (exists in the persistent shell, outside #calcpad-root).
$(function () {
  $('#cp-mode-interactive').on('click', () => setMode('interactive'));
  $('#cp-mode-final').on('click', () => setMode('final'));
  setMode(mode);
});
