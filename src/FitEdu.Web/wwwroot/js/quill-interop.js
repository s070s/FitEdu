// Quill.js interop for Blazor WYSIWYG editor component.
// Each editor instance is stored by element ID for multi-editor support.
//
// Quill itself (script + stylesheet) comes from jsDelivr and is loaded here, on the first editor
// that opens, not on every page: only the admin editors use it (P03-11). The addresses, the version
// path and the integrity hashes have to match the Content-Security-Policy, which allows only
// https://cdn.jsdelivr.net/npm/quill@2.0.3/ (SecurityHeadersMiddlewareExtensions); QuillLazyLoadTests
// checks that. Vendoring Quill under wwwroot/lib would remove the CDN (owner download question).
// How long a Quill file may take to load before the editor falls back to the plain text box. It has
// to stay well under Blazor's one-minute JS call timeout, which a hanging CDN would otherwise hit.
const LOAD_TIMEOUT_MILLISECONDS = 15000;

window.quillInterop = {
    _instances: {},

    // The two files a Quill editor needs. `pending` holds the load in progress or done; a failed load
    // clears it, so the next editor tries again.
    _script: {
        url: 'https://cdn.jsdelivr.net/npm/quill@2.0.3/dist/quill.js',
        integrity: 'sha256-9hV8cqybP1HN6tQmM1aIoCexJAXZ1qTardOKZ2stf/I=',
        pending: null
    },
    _styles: {
        url: 'https://cdn.jsdelivr.net/npm/quill@2.0.3/dist/quill.snow.css',
        integrity: 'sha256-HHlIzROqkvrGOQMZvB5eRhgj2hcVGdOnaNtWFk+HFjY=',
        pending: null
    },

    // Adds the file's element to the page once and resolves when the browser has loaded it (and
    // checked its integrity hash). Editors that open together share the one request.
    _loadFile: function (file, createElement) {
        if (!file.pending) {
            file.pending = new Promise(function (resolve, reject) {
                const element = createElement(file.url);
                element.integrity = file.integrity;
                element.crossOrigin = 'anonymous';
                let failed = false;
                // A hanging CDN would keep init past Blazor's one-minute JS call timeout, which
                // crashes the circuit; failing sooner leaves the plain text box.
                element.onload = function () {
                    clearTimeout(timer);
                    resolve();
                };
                element.onerror = function () {
                    clearTimeout(timer);
                    if (failed) {
                        return;
                    }
                    failed = true;
                    element.remove();
                    file.pending = null;
                    reject(new Error('Could not load ' + file.url));
                };
                const timer = setTimeout(element.onerror, LOAD_TIMEOUT_MILLISECONDS);
                document.head.appendChild(element);
            });
        }
        return file.pending;
    },

    _load: function () {
        const script = window.Quill
            ? Promise.resolve()
            : this._loadFile(this._script, function (url) {
                const element = document.createElement('script');
                element.src = url;
                return element;
            });
        const styles = this._loadFile(this._styles, function (url) {
            const element = document.createElement('link');
            element.rel = 'stylesheet';
            element.href = url;
            return element;
        });
        // Wait for both to finish, so a failed file is already cleared when the caller falls back.
        return Promise.allSettled([script, styles]).then(function (results) {
            const failed = results.find(function (result) { return result.status === 'rejected'; });
            if (failed) throw failed.reason;
        });
    },

    // Initialize a Quill editor on the given element, loading Quill first when this is the first
    // editor. The placeholder comes localised from Blazor. The returned promise resolves when the
    // editor is ready, which is what QuillEditor awaits before it reads or sets the content.
    init: async function (elementId, initialContent, placeholder) {
        const container = document.getElementById(elementId);
        if (!container) return;

        try {
            await this._load();
        } catch (error) {
            // The CDN is unreachable or the file did not match its hash: the editor stays usable as a
            // plain box holding the HTML, so the page can still be saved.
            console.error('Quill did not load; using a plain HTML text box instead.', error);
            this._useFallback(container, elementId, initialContent, placeholder);
            return;
        }

        // The component can be gone again while Quill was loading.
        if (!container.isConnected) return;

        const quill = new Quill(container, {
            theme: 'snow',
            modules: {
                toolbar: [
                    [{ 'header': [1, 2, 3, false] }],
                    ['bold', 'italic', 'underline', 'strike'],
                    [{ 'color': [] }, { 'background': [] }],
                    [{ 'list': 'ordered' }, { 'list': 'bullet' }],
                    ['blockquote', 'code-block'],
                    ['link', 'image', 'video'],
                    ['clean']
                ]
            },
            placeholder: placeholder || ''
        });

        if (initialContent) {
            quill.clipboard.dangerouslyPasteHTML(initialContent);
        }

        this._instances[elementId] = quill;
    },

    // A plain text box holding the HTML, used when Quill could not be loaded.
    _useFallback: function (container, elementId, initialContent, placeholder) {
        if (!container.isConnected) return;

        const textBox = document.createElement('textarea');
        textBox.className = 'form-control font-monospace';
        textBox.rows = 10;
        textBox.placeholder = placeholder || '';
        textBox.value = initialContent || '';
        container.appendChild(textBox);
        this._instances[elementId] = { fallback: textBox };
    },

    // Get HTML content from a Quill editor.
    getContent: function (elementId) {
        const quill = this._instances[elementId];
        if (!quill) return '';
        if (quill.fallback) return quill.fallback.value;
        const html = quill.root.innerHTML;
        // Quill returns <p><br></p> for empty content.
        return html === '<p><br></p>' ? '' : html;
    },

    // Set HTML content in a Quill editor.
    setContent: function (elementId, content) {
        const quill = this._instances[elementId];
        if (!quill) return;
        if (quill.fallback) {
            quill.fallback.value = content || '';
        } else if (!content) {
            quill.setContents([]);
        } else {
            quill.clipboard.dangerouslyPasteHTML(content);
        }
    },

    // Dispose a Quill editor instance.
    dispose: function (elementId) {
        delete this._instances[elementId];
    }
};
