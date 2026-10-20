(() => {
    'use strict';
    const navToggle = document.querySelector('[data-nav-toggle]');
    const sidebar = document.querySelector('.sidebar');
    const desktopNavigation = window.matchMedia('(min-width: 801px)');
    if (sidebar) sidebar.inert = !desktopNavigation.matches;
    function setNavigation(open) {
        document.body.classList.toggle('nav-open', open);
        if (sidebar) sidebar.inert = !open && !desktopNavigation.matches;
        navToggle?.setAttribute('aria-expanded', String(open));
        navToggle?.setAttribute('aria-label', open ? 'Мәзірді жабу' : 'Мәзірді ашу');
        if (open) sidebar?.querySelector('a')?.focus();
        else navToggle?.focus();
    }
    navToggle?.addEventListener('click', () => setNavigation(!document.body.classList.contains('nav-open')));
    document.querySelector('[data-nav-close]')?.addEventListener('click', () => setNavigation(false));
    document.addEventListener('keydown', event => {
        if (!document.body.classList.contains('nav-open')) return;
        if (event.key === 'Escape') setNavigation(false);
        if (event.key === 'Tab' && sidebar) {
            const focusable = [...sidebar.querySelectorAll('a,button,[tabindex="0"]')].filter(element => !element.hidden);
            const first = focusable[0], last = focusable.at(-1);
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
        }
    });
    desktopNavigation.addEventListener('change', event => {
        if (sidebar) sidebar.inert = !event.matches && !document.body.classList.contains('nav-open');
        if (event.matches) { document.body.classList.remove('nav-open'); navToggle?.setAttribute('aria-expanded', 'false'); }
    });
    document.querySelectorAll('[data-dismiss-notice]').forEach(button => button.addEventListener('click', () => button.closest('.notice')?.remove()));

    const grid = document.querySelector('[data-resource-grid]');
    const viewButtons = [...document.querySelectorAll('[data-view]')];
    function setView(view) {
        const list = view === 'list';
        grid?.classList.toggle('is-list', list);
        viewButtons.forEach(button => {
            const active = button.dataset.view === (list ? 'list' : 'grid');
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', String(active));
        });
        try { localStorage.setItem('edushelf-catalog-view', list ? 'list' : 'grid'); } catch { /* Storage is optional. */ }
    }
    if (viewButtons.length) {
        try { setView(localStorage.getItem('edushelf-catalog-view') || 'grid'); } catch { setView('grid'); }
        viewButtons.forEach(button => button.addEventListener('click', () => setView(button.dataset.view)));
    }

    const subjectSelect = document.querySelector('[data-subject-select]');
    const topicSelect = document.querySelector('[data-topic-select]');
    if (subjectSelect && topicSelect) {
        const topics = [...topicSelect.options].slice(1).map(option => option.cloneNode(true));
        function filterTopics(reset) {
            const previous = reset ? '' : topicSelect.value;
            topicSelect.replaceChildren(topicSelect.options[0]);
            topics.filter(option => !subjectSelect.value || option.dataset.subject === subjectSelect.value)
                .forEach(option => topicSelect.append(option.cloneNode(true)));
            topicSelect.value = [...topicSelect.options].some(option => option.value === previous) ? previous : '';
        }
        filterTopics(false);
        subjectSelect.addEventListener('change', () => filterTopics(true));
    }

    document.querySelectorAll('input[type=file][data-max-file-size]').forEach(input => {
        input.addEventListener('change', () => {
            const file = input.files?.[0];
            const name = input.closest('.upload-area')?.querySelector('[data-file-name]');
            input.setCustomValidity('');
            if (name) name.textContent = file ? file.name : '';
            if (!file) return;
            if (!file.name.toLowerCase().endsWith('.pdf')) input.setCustomValidity('PDF форматындағы файлды таңдаңыз.');
            else if (file.size > Number(input.dataset.maxFileSize)) input.setCustomValidity('Файлдың көлемі белгіленген шектен асып кетті.');
            if (!input.checkValidity()) input.reportValidity();
        });
    });

    function validateField(input) {
        if (input.type === 'hidden' || input.disabled) return true;
        const message = input.closest('.form-field')?.querySelector('[data-valmsg-for]');
        const required = input.dataset.valRequired;
        let error = '';
        if (required && !String(input.value).trim()) error = required;
        else if (input.type === 'email' && input.value && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(input.value)) error = input.dataset.valEmail || 'Электрондық пошта мекенжайын тексеріңіз.';
        else if (input.dataset.valLengthMin && input.value && input.value.length < Number(input.dataset.valLengthMin)) error = input.dataset.valLength || 'Енгізілген мән тым қысқа.';
        else if (input.dataset.valEqualtoOther && input.value) {
            const other = input.form?.elements.namedItem(input.dataset.valEqualtoOther.replace('*.', ''));
            if (other && other.value !== input.value) error = input.dataset.valEqualto || 'Құпиясөздер сәйкес келмейді.';
        }
        if (message) {
            message.textContent = error;
            message.classList.toggle('field-validation-error', !!error);
            message.classList.toggle('field-validation-valid', !error);
        }
        input.classList.toggle('input-validation-error', !!error);
        input.setAttribute('aria-invalid', String(!!error));
        return !error;
    }
    document.querySelectorAll('form').forEach(form => {
        const fields = [...form.querySelectorAll('input[data-val],select[data-val],textarea[data-val]')];
        fields.forEach(input => {
            input.addEventListener('blur', () => validateField(input));
            input.addEventListener('input', () => { if (input.classList.contains('input-validation-error')) validateField(input); });
        });
        form.addEventListener('submit', event => {
            if (event.defaultPrevented) return;
            const confirmMessage = form.dataset.confirm || event.submitter?.dataset.confirm;
            if (confirmMessage && !window.confirm(confirmMessage)) { event.preventDefault(); return; }
            const invalid = fields.filter(input => !validateField(input));
            if (invalid.length || !form.checkValidity()) {
                event.preventDefault();
                invalid[0]?.focus();
                form.reportValidity();
                return;
            }
            if (window.jQuery?.fn?.valid && !window.jQuery(form).valid()) {
                event.preventDefault();
                return;
            }
            if (form.matches('[data-download-form]') || form.method.toLowerCase() !== 'post') return;
            const button = event.submitter;
            if (!button) return;
            if (button.name) {
                const value = document.createElement('input'); value.type = 'hidden'; value.name = button.name; value.value = button.value; form.append(value);
            }
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
            const spinner = document.createElement('span'); spinner.className = 'loading-spinner'; spinner.setAttribute('aria-hidden', 'true');
            button.prepend(spinner);
            if (button.dataset.loadingText) {
                [...button.childNodes].filter(node => node.nodeType === Node.TEXT_NODE).forEach(node => node.textContent = ' ' + button.dataset.loadingText);
            }
        });
    });
    window.addEventListener('pageshow', event => {
        if (event.persisted) document.querySelectorAll('button[aria-busy="true"]').forEach(button => { button.disabled = false; button.removeAttribute('aria-busy'); button.querySelector('.loading-spinner')?.remove(); });
    });
})();
