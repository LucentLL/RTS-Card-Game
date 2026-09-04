// A REAL <input> element, laid over the canvas where a UI Toolkit text field is drawn.
//
// This exists because a WebGL player cannot open a phone's keyboard. TouchScreenKeyboard is not
// implemented on this platform, so a UI Toolkit TextField on a phone takes a tap, shows a caret,
// and then waits forever for keys that can never arrive - which is the whole of "it is impossible
// to host or join on mobile".
//
// It cannot be fixed from inside the player either. A browser opens its keyboard only when an
// input is focused DURING a user gesture, and Unity processes its input inside a
// requestAnimationFrame callback - long after the gesture that caused it has ended. So calling
// focus() from C# does nothing on a phone, whatever the C# is reacting to.
//
// The only thing that works is for the browser's own input to be the thing the finger lands on.
// So every field gets one, positioned over the field it belongs to and styled to match; the tap
// hits it directly, in gesture context, and the keyboard opens because the browser has no reason
// to think anything unusual happened. The player polls the value back.

var SrdTextEntryLibrary = {

  $SRDTE: {
    fields: {},
    next: 1,

    // Unity lays out in RENDER pixels (the drawing buffer); the DOM is in CSS pixels, and on a
    // phone those differ by the device pixel ratio. One canvas measurement converts both axes.
    scale: function () {
      var c = document.querySelector('#unity-canvas') || document.querySelector('canvas');
      if (!c || !c.clientWidth) return { k: 1, x: 0, y: 0 };
      var r = c.getBoundingClientRect();
      return { k: r.width / c.width, x: r.left, y: r.top };
    },
  },

  SrdTextCreate: function () {
    var id = SRDTE.next++;

    var el = document.createElement('input');
    el.type = 'text';
    el.autocapitalize = 'off';
    el.autocomplete = 'off';
    el.autocorrect = 'off';
    el.spellcheck = false;

    var s = el.style;
    s.position = 'fixed';
    s.margin = '0';
    s.display = 'none';
    s.zIndex = '20';
    s.boxSizing = 'border-box';
    s.border = '1px solid rgba(107,102,77,0.7)';
    s.borderRadius = '4px';
    s.background = 'rgb(26,28,38)';
    s.color = 'rgb(232,226,208)';
    s.outline = 'none';
    s.fontFamily = 'system-ui, -apple-system, Segoe UI, Roboto, sans-serif';

    // A focus ring the player can actually see. The complaint that started this was not only the
    // keyboard - it was that a selected box looked exactly like an unselected one.
    el.addEventListener('focus', function () {
      s.borderColor = 'rgb(255,217,102)';
      s.background = 'rgb(34,36,48)';
    });
    el.addEventListener('blur', function () {
      s.borderColor = 'rgba(107,102,77,0.7)';
      s.background = 'rgb(26,28,38)';
    });

    // KEEP THE KEYS. Unity listens for keydown/keypress/keyup on the DOCUMENT and calls
    // preventDefault() on almost all of them, so the game can have WASD and the arrows without
    // the page scrolling. That is fatal to a real input: preventDefault on a keydown is exactly
    // what stops the character being inserted, so the field took focus, showed a caret, and then
    // silently dropped every keystroke into the game instead - typing scrolled the deck list.
    //
    // Stopping propagation AT THE INPUT settles it. Unity listens on an ancestor, so an event
    // that never leaves the input never reaches it, and the browser is left to do the ordinary
    // thing with a key pressed in a focused text box.
    ['keydown', 'keypress', 'keyup', 'input', 'paste', 'cut',
     'pointerdown', 'pointerup', 'mousedown', 'mouseup', 'touchstart', 'touchend',
     'click', 'wheel'].forEach(function (name) {
      el.addEventListener(name, function (ev) { ev.stopPropagation(); });
    });

    // Enter dismisses the phone keyboard rather than submitting anything - there is no form here,
    // and a keyboard that will not go away hides the thing you were typing into.
    el.addEventListener('keydown', function (ev) {
      if (ev.key === 'Enter' || ev.key === 'Escape') el.blur();
    });

    document.body.appendChild(el);
    SRDTE.fields[id] = el;
    return id;
  },

  SrdTextConfig: function (id, valuePtr, placeholderPtr, maxLen) {
    var el = SRDTE.fields[id];
    if (!el) return;
    el.value = UTF8ToString(valuePtr);
    el.placeholder = UTF8ToString(placeholderPtr);
    if (maxLen > 0) el.maxLength = maxLen;
  },

  // Rect in Unity render pixels, top-left origin - which is what VisualElement.worldBound gives
  // on a ConstantPixelSize panel at scale 1.
  SrdTextPlace: function (id, x, y, w, h, fontPx) {
    var el = SRDTE.fields[id];
    if (!el) return;
    // FULLSCREEN re-parents everything. The player goes fullscreen on the first touch of a
    // phone, and a fullscreen element renders alone - a sibling left on document.body simply
    // stops being drawn, so the field would vanish the moment it was most needed. Following the
    // fullscreen element costs one comparison a frame.
    var host = document.fullscreenElement || document.body;
    if (el.parentNode !== host) host.appendChild(el);

    var m = SRDTE.scale();
    el.style.display = 'block';
    el.style.left = (m.x + x * m.k) + 'px';
    el.style.top = (m.y + y * m.k) + 'px';
    el.style.width = (w * m.k) + 'px';
    el.style.height = (h * m.k) + 'px';
    el.style.fontSize = (fontPx * m.k) + 'px';
    el.style.paddingLeft = (6 * m.k) + 'px';
    el.style.paddingRight = (6 * m.k) + 'px';
  },

  SrdTextHide: function (id) {
    var el = SRDTE.fields[id];
    if (el) el.style.display = 'none';
  },

  // Into a buffer the caller owns, so nothing has to be freed across the boundary. Returns the
  // byte length written, not counting the terminator.
  SrdTextRead: function (id, into, max) {
    var el = SRDTE.fields[id];
    if (!el || max <= 0) return 0;
    var v = el.value || '';
    var n = lengthBytesUTF8(v);
    while (n >= max && v.length > 0) { v = v.slice(0, -1); n = lengthBytesUTF8(v); }
    stringToUTF8(v, into, max);
    return n;
  },

  SrdTextWrite: function (id, valuePtr) {
    var el = SRDTE.fields[id];
    if (!el) return;
    var v = UTF8ToString(valuePtr);
    if (el.value !== v) el.value = v;
  },

  SrdTextDestroy: function (id) {
    var el = SRDTE.fields[id];
    if (!el) return;
    if (el.parentNode) el.parentNode.removeChild(el);
    delete SRDTE.fields[id];
  },
};

autoAddDeps(SrdTextEntryLibrary, '$SRDTE');
mergeInto(LibraryManager.library, SrdTextEntryLibrary);
