# Sahte SMS sunucusu (geliştirme/test). Temel imajlar digest ile sabitlenmiştir.
FROM golang:1.27.4-alpine@sha256:70bd7ddea8b6cbe5d6943cde279d6b67feb10cc924fb9100d1d8757c1575b280 AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 GOWORK=off go build -trimpath -ldflags="-s -w" -o /out/fake-sms ./cmd/fake-sms

FROM gcr.io/distroless/static-debian12:nonroot@sha256:afa5c872c891853ca7fcf1f12c3edb23f7eeef36189728842dd51042ff57f7ab
COPY --from=build /out/fake-sms /fake-sms
EXPOSE 8026 8027
ENTRYPOINT ["/fake-sms"]
