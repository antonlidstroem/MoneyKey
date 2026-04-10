// wwwroot/js/qrcode.js
// Renders a QR code into a <canvas> element using the qrcodejs CDN library.
//
// CDN dependency — add both tags to wwwroot/index.html before </body>:
//
//   <script src="https://cdnjs.cloudflare.com/ajax/libs/qrcodejs/1.0.0/qrcode.min.js"
//           integrity="sha512-CNgIRecGo7nphbeZ04Sc13ka07paqdeTu0WR1IM4kNcpmBAUSHSE7ApupMutZSmvZ8XKkXoV7XMoGdI1NeHMeQ=="
//           crossorigin="anonymous" referrerpolicy="no-referrer"></script>
//   <script src="js/qrcode.js"></script>
//
// Usage from Blazor:
//   await JS.InvokeVoidAsync("renderQrCode", "qr-canvas", shareUrl);

window.renderQrCode = function (canvasId, text, attempt) {
    attempt = attempt || 0;

    var canvas = document.getElementById(canvasId);
    if (!canvas) {
        // Canvas not in the DOM yet (Blazor may not have rendered it).
        // Retry up to 10 times with 100 ms spacing.
        if (attempt < 10) {
            setTimeout(function () {
                window.renderQrCode(canvasId, text, attempt + 1);
            }, 100);
        }
        return;
    }

    if (typeof QRCode === "undefined") {
        // Library not loaded yet — retry.
        if (attempt < 20) {
            setTimeout(function () {
                window.renderQrCode(canvasId, text, attempt + 1);
            }, 150);
        }
        return;
    }

    // Clear any previous QR code rendered into this canvas.
    var ctx = canvas.getContext("2d");
    if (ctx) ctx.clearRect(0, 0, canvas.width, canvas.height);

    // qrcodejs uses a container div; wrap the canvas temporarily.
    var wrapper = document.createElement("div");
    wrapper.style.display = "none";
    document.body.appendChild(wrapper);

    try {
        new QRCode(wrapper, {
            text:          text,
            width:         canvas.width  || 200,
            height:        canvas.height || 200,
            colorDark:     "#263238",
            colorLight:    "#ffffff",
            correctLevel:  QRCode.CorrectLevel.M
        });

        // qrcodejs appends an <img> or <canvas> inside wrapper.
        var generated = wrapper.querySelector("canvas") || wrapper.querySelector("img");
        if (generated) {
            if (generated.tagName === "CANVAS") {
                ctx.drawImage(generated, 0, 0, canvas.width, canvas.height);
            } else {
                // IMG tag — wait for load then draw.
                generated.onload = function () {
                    ctx.drawImage(generated, 0, 0, canvas.width, canvas.height);
                };
            }
        }
    } catch (e) {
        console.warn("renderQrCode error:", e);
    } finally {
        document.body.removeChild(wrapper);
    }
};
