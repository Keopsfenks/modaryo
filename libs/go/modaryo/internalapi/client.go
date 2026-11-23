package internalapi

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"

	"github.com/google/uuid"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/problem"
)

// maxResponseBytes caps what the client reads from a response.
const maxResponseBytes = 16 << 20

// Client calls another service's "/internal/*" endpoints with signed requests.
type Client struct {
	baseURL string
	service string
	secret  []byte
	hc      *http.Client
	now     func() time.Time
}

// NewClient creates a client. service is this service's name (X-Modaryo-Service); hc may be
// nil (a client with a 30 s timeout is used).
func NewClient(baseURL, service string, secret []byte, hc *http.Client) *Client {
	if hc == nil {
		hc = &http.Client{Timeout: 30 * time.Second}
	}
	return &Client{baseURL: strings.TrimRight(baseURL, "/"), service: service, secret: secret, hc: hc, now: time.Now}
}

// Do sends a signed request. path starts with "/" and may carry a query that is sent as
// given. body (nil = none) is JSON-encoded. tenant (when not uuid.Nil) and idemKey (when
// non-empty) become signed headers. A 2xx response is decoded into out (when non-nil);
// any other response becomes a *problem.Error.
func (c *Client) Do(ctx context.Context, method, path string, tenant uuid.UUID, idemKey string, body any, out any) error {
	method = strings.ToUpper(method)
	var payload []byte
	if body != nil {
		var err error
		if payload, err = json.Marshal(body); err != nil {
			return fmt.Errorf("internalapi: encode body: %w", err)
		}
	}

	u, err := url.Parse(c.baseURL + path)
	if err != nil {
		return fmt.Errorf("internalapi: bad url: %w", err)
	}
	// The signed target is what Go puts on the request line, so server and client see the same bytes.
	pathAndQuery := u.RequestURI()

	req, err := http.NewRequestWithContext(ctx, method, u.String(), bytes.NewReader(payload))
	if err != nil {
		return err
	}
	if len(payload) > 0 {
		req.Header.Set("Content-Type", "application/json")
	}
	req.Header.Set("Accept", "application/json")

	tenantHeader := ""
	if tenant != uuid.Nil {
		tenantHeader = tenant.String()
		req.Header.Set(HeaderTenant, tenantHeader)
	}
	if idemKey != "" {
		req.Header.Set(HeaderIdempotencyKey, idemKey)
	}
	ts := c.now().Unix()
	req.Header.Set(HeaderService, c.service)
	req.Header.Set(HeaderTimestamp, strconv.FormatInt(ts, 10))
	req.Header.Set(HeaderSignature, Sign(c.secret, method, pathAndQuery, ts, tenantHeader, idemKey, payload))

	resp, err := c.hc.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	raw, err := io.ReadAll(io.LimitReader(resp.Body, maxResponseBytes))
	if err != nil {
		return err
	}

	if resp.StatusCode < 200 || resp.StatusCode > 299 {
		return decodeProblem(resp.StatusCode, raw)
	}
	if out != nil && len(bytes.TrimSpace(raw)) > 0 {
		if err := json.Unmarshal(raw, out); err != nil {
			return fmt.Errorf("internalapi: decode response: %w", err)
		}
	}
	return nil
}

func decodeProblem(status int, raw []byte) error {
	var d problem.Details
	if json.Unmarshal(raw, &d) != nil || (d.Title == "" && d.Code == "" && d.Detail == "") {
		d = problem.Details{Title: http.StatusText(status)}
	}
	d.Status = status
	return &problem.Error{Details: d}
}
