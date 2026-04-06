// fileDownload.js
// Called from Blazor via JS interop after fetching a file through
// the authenticated HttpClient. Base64 data is decoded client-side
// and a Blob URL is used to trigger the browser's save dialog.
window.downloadFile = function (filename, contentType, base64Data) {
    const byteChars = atob(base64Data);
    const byteNums  = new Array(byteChars.length);
    for (let i = 0; i < byteChars.length; i++) {
        byteNums[i] = byteChars.charCodeAt(i);
    }
    const bytes = new Uint8Array(byteNums);
    const blob  = new Blob([bytes], { type: contentType });
    const url   = URL.createObjectURL(blob);

    const a      = document.createElement('a');
    a.href       = url;
    a.download   = filename;
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);

    // Revoke after a short delay to allow the download to start.
    setTimeout(() => URL.revokeObjectURL(url), 10000);
};
