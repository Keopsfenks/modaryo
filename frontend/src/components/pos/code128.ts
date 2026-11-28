// Code128 (küme B) kodlayıcı: fiş numarasındaki ASCII karakterleri çubuk genişliklerine çevirir.

// Her giriş 6 sayıdır (çubuk, boşluk dönüşümlü genişlikler); 106 durdurma deseni 7 sayıdır.
export const PATTERNS: string[] =
    "212222,222122,222221,121223,121322,131222,122213,122312,132212,221213,221312,231212,112232,122132,122231,113222,123122,123221,223211,221132,221231,213212,223112,312131,311222,321122,321221,312212,322112,322211,212123,212321,232121,111323,131123,131321,112313,132113,132311,211313,231113,231311,112133,112331,132131,113123,113321,133121,313121,211331,231131,213113,213311,213131,311123,311321,331121,312113,312311,332111,314111,221411,431111,111224,111422,121124,121421,141122,141221,112214,112412,122114,122411,142112,142211,241211,221114,413111,241112,134111,111242,121142,121241,114212,124112,124211,411212,421112,421211,212141,214121,412121,111143,111341,131141,114113,114311,411113,411311,113141,114131,311141,411131,211412,211214,211232,2331112".split(",");

const START_B = 104;
const STOP = 106;

/** Metni Code128-B değerlerine (başlangıç, veri, sağlama, durdurma) çevirir; yazdırılamayan karakterler atlanır. */
export function code128Values(text: string): number[] {
    const data = [...text].map((c) => c.charCodeAt(0) - 32).filter((v) => v >= 0 && v <= 94);
    let sum = START_B;
    data.forEach((v, i) => {
        sum += v * (i + 1);
    });
    return [START_B, ...data, sum % 103, STOP];
}

/** Çubuk/boşluk genişlikleri dizisi (modül birimi); ilk eleman bir çubuktur. */
export function code128Widths(text: string): number[] {
    return code128Values(text).flatMap((v) => PATTERNS[v].split("").map(Number));
}
