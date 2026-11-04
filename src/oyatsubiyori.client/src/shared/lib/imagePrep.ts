/**
 * 画像の前処理（T0-13。FR-FLYER-01・01a・01b・08、FR-PROD-05、NFR-PERF-04、NFR-UX-02）。
 * - JPEG・長辺 2576px かつ約 3.75MP 以下・品質 0.85 に変換（createImageBitmap で EXIF の回転も直す）
 * - HEIC はブラウザで読めたときだけ変換し、読めなければ「JPEG で保存し直してください」
 * - PDF は pdf.js でページごとの画像にする
 * 画像はメモリ（コンポーネントの状態）にだけ持ち、localStorage などには保存しない（U-02）。
 */

/** 1ファイルの上限（バイト） */
export const MAX_FILE_BYTES = 20 * 1024 * 1024;

const MAX_LONG_SIDE = 2576;
const MAX_PIXELS = 3_750_000;
const JPEG_QUALITY = 0.85;

/** 送る準備ができた画像 */
export interface PreparedImage {
    id: string;
    blob: Blob;
    previewUrl: string;
    label: string;
}

/** 前処理の失敗（理由は日本語） */
export class ImagePrepError extends Error {
    constructor(message: string) {
        super(message);
        this.name = 'ImagePrepError';
    }
}

const newId = () => (crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`);

const isHeic = (file: File) => /heic|heif/i.test(file.type) || /\.(heic|heif)$/i.test(file.name);

export const isPdf = (file: File) => file.type === 'application/pdf' || /\.pdf$/i.test(file.name);

/** 縮める大きさを求める（長辺と画素数の両方の上限） */
const fitSize = (width: number, height: number) => {
    let scale = Math.min(1, MAX_LONG_SIDE / Math.max(width, height));
    if (width * height * scale * scale > MAX_PIXELS) {
        scale = Math.sqrt(MAX_PIXELS / (width * height));
    }
    return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) };
};

const canvasToJpeg = (canvas: HTMLCanvasElement): Promise<Blob> =>
    new Promise((resolve, reject) => {
        canvas.toBlob(
            (blob) => (blob ? resolve(blob) : reject(new ImagePrepError('画像を変換できませんでした。別の画像で試してください。'))),
            'image/jpeg',
            JPEG_QUALITY,
        );
    });

const drawToJpeg = async (source: CanvasImageSource, width: number, height: number): Promise<Blob> => {
    const size = fitSize(width, height);
    const canvas = document.createElement('canvas');
    canvas.width = size.width;
    canvas.height = size.height;
    const context = canvas.getContext('2d');
    if (!context) {
        throw new ImagePrepError('画像を変換できませんでした。別の画像で試してください。');
    }
    context.drawImage(source, 0, 0, size.width, size.height);
    const blob = await canvasToJpeg(canvas);
    canvas.width = 0;
    canvas.height = 0;
    return blob;
};

/** ファイルの大きさと形式を確かめる（理由は日本語。NFR-UX-02a） */
export const checkFile = (file: File, acceptPdf: boolean): string | null => {
    if (file.size > MAX_FILE_BYTES) {
        return `「${file.name}」は大きすぎます（20MB まで）。小さい画像にして、もう一度選んでください。`;
    }
    if (isPdf(file)) {
        return acceptPdf ? null : `「${file.name}」は PDF です。ここでは画像（写真）を選んでください。`;
    }
    if (!file.type.startsWith('image/') && !isHeic(file)) {
        return `「${file.name}」は画像ではありません。写真（JPEG・PNG など）${acceptPdf ? 'か PDF ' : ''}を選んでください。`;
    }
    return null;
};

/** 画像を JPEG に変換する */
export const prepareImage = async (file: File): Promise<PreparedImage> => {
    let bitmap: ImageBitmap;
    try {
        bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
    } catch {
        if (isHeic(file)) {
            throw new ImagePrepError(`「${file.name}」（HEIC）はこのブラウザで読めません。写真を JPEG で保存し直してから選んでください。`);
        }
        throw new ImagePrepError(`「${file.name}」を読めませんでした。別の画像で試してください。`);
    }
    try {
        const blob = await drawToJpeg(bitmap, bitmap.width, bitmap.height);
        return { id: newId(), blob, previewUrl: URL.createObjectURL(blob), label: file.name };
    } finally {
        bitmap.close();
    }
};

/** PDF をページごとの画像にする（pdf.js。worker は Vite で読み込む） */
export const preparePdfPages = async (file: File): Promise<PreparedImage[]> => {
    const pdfjs = await import('pdfjs-dist');
    const worker = await import('pdfjs-dist/build/pdf.worker.min.mjs?url');
    pdfjs.GlobalWorkerOptions.workerSrc = worker.default;

    const loadingTask = pdfjs.getDocument({ data: new Uint8Array(await file.arrayBuffer()) });
    let document;
    try {
        document = await loadingTask.promise;
    } catch {
        throw new ImagePrepError(`「${file.name}」を PDF として読めませんでした。別のファイルで試してください。`);
    }

    const pages: PreparedImage[] = [];
    try {
        for (let number = 1; number <= document.numPages; number++) {
            const page = await document.getPage(number);
            const base = page.getViewport({ scale: 1 });
            const size = fitSize(base.width * 4, base.height * 4);
            const viewport = page.getViewport({ scale: size.width / base.width });
            const canvas = window.document.createElement('canvas');
            canvas.width = Math.round(viewport.width);
            canvas.height = Math.round(viewport.height);
            await page.render({ canvas, viewport }).promise;
            const blob = await canvasToJpeg(canvas);
            canvas.width = 0;
            canvas.height = 0;
            pages.push({ id: newId(), blob, previewUrl: URL.createObjectURL(blob), label: `${file.name} ${number}ページ` });
        }
    } finally {
        await loadingTask.destroy();
    }
    return pages;
};

/** プレビューの URL を捨てる（画面を離れたとき・外したとき） */
export const releaseImages = (images: PreparedImage[]) => {
    images.forEach((image) => URL.revokeObjectURL(image.previewUrl));
};
