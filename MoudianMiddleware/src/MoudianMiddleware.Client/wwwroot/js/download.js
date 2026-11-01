window.downloadFile = (fileName, contentType, base64) => {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([bytes], { type: contentType }));
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(link.href);
};
