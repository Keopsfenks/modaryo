# İleti Merkezi API şekilleri (sahte SMS sağlayıcısı)

- Kaynak (yeniden doğrulandı, 2026-11-23):
  - https://www.iletimerkezi.com/en/docs/api/send-sms
  - https://www.iletimerkezi.com/en/docs/api/overview
  - https://www.iletimerkezi.com/en/docs/api/get-report
  - https://www.iletimerkezi.com/en/docs/api/get-balance
  - https://www.iletimerkezi.com/en/docs/api/error-codes
- Erişim tarihi: 2026-11-23. Bu dosyadaki "doğrulanmış" işaretleri dokümantasyon sayfalarındaki
  açık ifadelere dayanır. "Doğrulanmadı" olanlar canlı hesapta kontrol edilmelidir.

## JSON türleri (dokümantasyona göre)

- Yanıt `response.status.code`: tamsayı (number). `order.id`: metin (string).
- `iys`: metin ("1" / "0"). `sendDateTime`: metin, isteğe bağlı, `dd/MM/yyyy HH:mm`.
- Rapor `page`, `rowCount`: metin. Rapor `order.id`: metin. `order.status`, `total`, `delivered`,
  `undelivered`, `waiting`, `price`, `message[].status`: tamsayı.
- Bakiye `balance.amount`: sayı (ondalık). `balance.sms`: tamsayı. Dokümanın eski sürümleri
  bunları metin gösterir; canlı API sayı döner.

## Fixture bazında alanlar

### send-request.json (doğrulandı, bir alan hariç)
- `request.authentication.key`, `hash`: doğrulandı (zorunlu).
- `request.order.sender`: doğrulandı (zorunlu, en fazla 11 karakter, Türkçe karakter yok).
- `request.order.iys`: doğrulandı (zorunlu, "1" veya "0").
- `request.order.iysList`: doğrulandı (yalnızca `iys` "1" ise, BIREYSEL veya TACIR).
- `request.order.sendDateTime`: doğrulandı (isteğe bağlı). Sunucu biçimini denetler, ama ileri tarihli
  gönderim emüle edilmez; mesaj hemen kaydedilir ve 1 s sonra iletildi olur.
- `request.order.message.text`: doğrulandı (zorunlu).
- `request.order.message.receipents.number`: doğrulandı (zorunlu, "receipents" yazımı dokümanda da böyle).
  Kabul edilen biçimler: `905XXXXXXXXX`, `+905XXXXXXXXX`, `5XXXXXXXXX`.
  Doğrulanmadı: `05XXXXXXXXX` (baştaki 0). Dokümantasyon bu biçimi listelemiyor, bu yüzden reddedilir (466).

### send-response.json (doğrulandı)
- `response.status.code` (200, tamsayı), `response.status.message`: doğrulandı.
- `response.order.id` (metin): doğrulandı.

### report-request.json (doğrulandı)
- `request.authentication.*`: doğrulandı.
- `request.order.id` (metin, zorunlu): doğrulandı.
- `request.order.page` (metin, isteğe bağlı, varsayılan "1"): doğrulandı. Sunucu sayfalamayı emüle etmez;
  değer kabul edilir ve tüm satırlar döner.
- `request.order.rowCount` (metin, isteğe bağlı, varsayılan "1000", en çok "1000"): doğrulandı. Aynı şekilde yok sayılır.

### report-response.json (kısmen doğrulandı)
- `response.status.code`, `message`: doğrulandı.
- `response.order.id` (metin), `status` (114), `total`, `delivered`, `undelivered`, `waiting`, `price` (tamsayı): doğrulandı.
- `response.order.submitAt`, `sendAt` (`yyyy-MM-dd HH:mm:ss`) ve `sender`: örnekten alındı, doğrulanmadı.
- `response.order.message` (dizi anahtarı): dokümanın örneği ve dokümanın "message[]" tanımı; doğrulanmadı,
  dokümanın kendisi canlı yanıtla kontrol edilmesini öneriyor.
- `message[].number` (`+90…`), `message[].status` (110/111/112, tamsayı): doğrulandı.

### balance-request.json (doğrulandı)
- `request.authentication.key`, `hash`: doğrulandı. Başka alan yok.

### balance-response.json (doğrulandı)
- `response.status.code`, `message`: doğrulandı.
- `response.balance.amount` (sayı, 10.584), `balance.sms` (tamsayı, 67): doğrulandı (dokümantasyon örneği).

### auth-error-response.json (kısmen doğrulandı)
- `response.status.code` (401) ve `message` ("Üyelik bilgileri hatalı"): doğrulandı (hata kodları sayfası).
- Gövde biçimi (zarf): doğrulanmadı. Dokümantasyon yalnızca kod ve mesajı listeler.
- HTTP durum kodu 401: dokümantasyon hatayı "401" olarak etiketler, ama aktarım seviyesinde HTTP 401
  döndüğünü açıkça söylemez. UNVERIFIED.

### insufficient-balance-response.json (kısmen doğrulandı)
- `response.status.code` (402) ve `message` ("Bakiye yetersiz"): doğrulandı (hata kodları sayfası).
- Gövde ve HTTP durum kodu: doğrulanmadı. Dokümantasyon 402 için ayrıntı vermiyor. Sunucu HTTP 200 döner.

## Hata HTTP durumları (sunucu davranışı)

- İş hataları (452, 454, 455, 457, 459, 460, 466, 468, 469, 402) HTTP 200 ile, kod gövdede döner.
  Dokümantasyon bu hatalar için HTTP durumu belirtmiyor; bu bir varsayımdır.
- Kimlik doğrulama hatası HTTP 401 ile döner (dokümantasyon "401" olarak etiketler, bkz. yukarı).
- Bozuk JSON veya bilinmeyen alan HTTP 400 ile döner (`response.status.code` 400).
- **Adaptör yalnızca `response.status.code` değerine göre dallanmalıdır**, HTTP durumuna göre değil.
