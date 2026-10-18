window.copyToClipboard = async function (text) {
    await navigator.clipboard.writeText(text);
    return true;
};

// A flag picker is a <details> drop-down, which the browser closes only on its own summary; a click anywhere else closes it too.
document.addEventListener("click", function (event) {
    document.querySelectorAll("details.flag-picker[open]").forEach(function (picker) {
        if (!picker.contains(event.target)) {
            picker.removeAttribute("open");
        }
    });
});
