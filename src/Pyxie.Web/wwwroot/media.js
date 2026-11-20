export function bind(input, receiver, url) {
    let disposed = false;
    let request = null;
    let working = false;
    const call = async (name, ...args) => {
        if (!disposed) return await receiver.invokeMethodAsync(name, ...args);
    };
    const changed = async () => {
        if (working || disposed) return;
        const files = Array.from(input.files || []);
        input.value = "";
        if (!files.length) return;
        working = true;
        try {
            if (!await call("MediaUploadStarted")) return;
            const response = await fetch("media/token", { credentials: "same-origin" });
            if (!response.ok) throw new Error("The upload could not start. Reload the page.");
            const token = await response.json();
            for (const file of files) {
                if (disposed) break;
                await call("MediaUploadProgress", file.name, 0);
                try {
                    const result = await new Promise((resolve, reject) => {
                        request = new XMLHttpRequest();
                        request.open("POST", `${url}?name=${encodeURIComponent(file.name)}`);
                        request.setRequestHeader(token.header || "RequestVerificationToken", token.token);
                        request.setRequestHeader("Content-Type", "application/octet-stream");
                        let last = -1;
                        request.upload.onprogress = event => {
                            const progress = event.lengthComputable ? Math.floor(event.loaded / event.total * 100) : 0;
                            if (progress !== last) {
                                last = progress;
                                void call("MediaUploadProgress", file.name, progress).catch(() => {});
                            }
                        };
                        request.onload = () => {
                            let body;
                            try { body = JSON.parse(request.responseText); } catch { body = {}; }
                            if (request.status >= 200 && request.status < 300) resolve(body);
                            else reject(new Error(body.error || "The file could not be uploaded. Check its size and try again."));
                        };
                        request.onerror = () => reject(new Error("The connection was interrupted. Try the file again."));
                        request.onabort = () => reject(new Error("Upload cancelled."));
                        request.send(file);
                    });
                    await call("MediaUploadCompleted", JSON.stringify(result));
                } catch (error) {
                    await call("MediaUploadFailed", file.name, error.message);
                }
            }
        } catch (error) {
            await call("MediaUploadFailed", "Upload", error.message);
        } finally {
            working = false;
            request = null;
            await call("MediaUploadEnded");
        }
    };
    input.addEventListener("change", changed);
    return {
        choose() { if (!working && !disposed) input.click(); },
        dispose() { disposed = true; request?.abort(); input.removeEventListener("change", changed); }
    };
}
