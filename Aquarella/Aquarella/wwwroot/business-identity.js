// Legacy profile reader plus presentation-only theme application/cache; SQLite is authoritative.
(() => {
    const key = 'aquarella.business-profile.v1';
    const defaults = { primaryColor: '#F7F9F6', secondaryColor: '#28745B', tertiaryColor: '#203E32' };
    const hex = value => typeof value === 'string' && /^#[\da-f]{6}$/i.test(value);
    const rgb = value => [1, 3, 5].map(i => parseInt(value.slice(i, i + 2), 16));
    const luminance = value => rgb(value).map(v => {
        const s = v / 255;
        return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    }).reduce((sum, v, i) => sum + v * [0.2126, 0.7152, 0.0722][i], 0);
    const contrast = (a, b) => {
        const x = luminance(a), y = luminance(b);
        return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
    };
    const readable = background => contrast('#17241D', background) >= contrast('#FFFFFF', background) ? '#17241D' : '#FFFFFF';
    const mix = (color, target, amount) => '#' + rgb(color).map((v, i) =>
        Math.round(v * (1 - amount) + rgb(target)[i] * amount).toString(16).padStart(2, '0')).join('');
    function apply(profile) {
        const primary = hex(profile?.primaryColor) ? profile.primaryColor : defaults.primaryColor;
        const secondary = hex(profile?.secondaryColor) ? profile.secondaryColor : defaults.secondaryColor;
        const tertiary = hex(profile?.tertiaryColor) ? profile.tertiaryColor : defaults.tertiaryColor;
        const text = readable(primary);
        const surface = mix(primary, text === '#FFFFFF' ? '#000000' : '#FFFFFF', 0.8);
        const surfaceText = readable(surface);
        const heading = contrast(secondary, primary) >= 4.5 && contrast(secondary, surface) >= 4.5 ? secondary : text;
        const accentText = contrast(tertiary, surface) >= 4.5 ? tertiary : surfaceText;
        const semantic = (light, dark) => contrast(light, surface) >= 4.5 ? light
            : contrast(dark, surface) >= 4.5 ? dark : surfaceText;
        const values = {
            primary, secondary, tertiary, surface, 'surface-text': surfaceText, text,
            heading, 'accent-text': accentText, 'on-secondary': readable(secondary),
            border: mix(tertiary, surface, 0.7), muted: mix(text, primary, 0.23),
            'surface-muted': mix(surfaceText, surface, 0.23),
            'accent-surface': mix(tertiary, surface, 0.92),
            success: semantic('#28754B', '#83D9A0'),
            warning: semantic('#946400', '#F2CE78'),
            danger: semantic('#B83232', '#FFAAA0')
        };
        for (const [name, value] of Object.entries(values)) document.documentElement.style.setProperty(`--${name}`, value);
    }
    function load() {
        const raw = localStorage.getItem(key);
        if (!raw) return null;
        try {
            const profile = JSON.parse(raw);
            if (!profile || typeof profile.name !== 'string' || !profile.name.trim()
                || !hex(profile.primaryColor) || !hex(profile.secondaryColor) || !hex(profile.tertiaryColor)) return null;
            return profile;
        } catch { return null; }
    }
    window.aquarellaIdentity = {
        load,
        display(profile) { window.aquarellaCurrentProfile = profile; apply(profile); try { localStorage.setItem("aquarella.theme-cache.v1", JSON.stringify({ primaryColor: profile.primaryColor, secondaryColor: profile.secondaryColor, tertiaryColor: profile.tertiaryColor })); } catch { } },
        refresh() { try { apply(window.aquarellaCurrentProfile ?? JSON.parse(localStorage.getItem("aquarella.theme-cache.v1") ?? "null") ?? load()); } catch { apply(null); } },

    };
    try { apply(window.aquarellaCurrentProfile ?? JSON.parse(localStorage.getItem("aquarella.theme-cache.v1") ?? "null") ?? load()); } catch { apply(null); }
})();



