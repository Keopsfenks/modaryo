// Package config loads a service's configuration from MODARYO_* environment variables.
package config

import (
	"bufio"
	"errors"
	"fmt"
	"os"
	"reflect"
	"strconv"
	"strings"
	"time"
)

const (
	envFile = ".env.local"
	bom     = "\xef\xbb\xbf"
)

var durationType = reflect.TypeOf(time.Duration(0))

// Load fills a T from environment variables. Each exported field carries the tags
// `env:"NAME"` (the variable is prefix+NAME), `required:"true"` and `default:"..."`. Fields
// without an env tag are skipped. Supported kinds: string, int, int64, bool, float64,
// time.Duration (Go syntax, e.g. "15s") and []string (comma separated).
//
// When MODARYO_ENV is empty or "development", ".env.local" in the working directory is read as
// well; real environment variables always win over the file. All missing required variables are
// reported in one error. A parse error names the variable but never echoes its value.
func Load[T any](prefix string) (T, error) {
	var out T
	v := reflect.ValueOf(&out).Elem()
	if v.Kind() != reflect.Struct {
		return out, errors.New("config: T bir struct olmalı")
	}

	file := map[string]string{}
	if env := os.Getenv("MODARYO_ENV"); env == "" || env == "development" {
		var err error
		if file, err = readEnvFile(envFile); err != nil {
			return out, err
		}
	}
	lookup := func(name string) (string, bool) {
		if s, ok := os.LookupEnv(name); ok && s != "" {
			return s, true
		}
		if s, ok := file[name]; ok && s != "" {
			return s, true
		}
		return "", false
	}

	var missing, invalid []string
	t := v.Type()
	for i := 0; i < t.NumField(); i++ {
		f := t.Field(i)
		key, ok := f.Tag.Lookup("env")
		if !ok || key == "" || !f.IsExported() {
			continue
		}
		name := prefix + key
		raw, found := lookup(name)
		if !found {
			if d, has := f.Tag.Lookup("default"); has {
				raw, found = d, true
			}
		}
		if !found {
			if f.Tag.Get("required") == "true" {
				missing = append(missing, name)
			}
			continue
		}
		if err := assign(v.Field(i), raw); err != nil {
			if errors.Is(err, errUnsupported) {
				return out, fmt.Errorf("config: %s alanı için desteklenmeyen tür %s", f.Name, f.Type)
			}
			invalid = append(invalid, name)
		}
	}

	var msgs []string
	if len(missing) > 0 {
		msgs = append(msgs, "Eksik yapılandırma: "+strings.Join(missing, ", "))
	}
	if len(invalid) > 0 {
		msgs = append(msgs, "Geçersiz yapılandırma değeri: "+strings.Join(invalid, ", "))
	}
	if len(msgs) > 0 {
		return out, errors.New(strings.Join(msgs, "; "))
	}
	return out, nil
}

var errUnsupported = errors.New("unsupported kind")

func assign(fv reflect.Value, raw string) error {
	if fv.Type() == durationType {
		d, err := time.ParseDuration(strings.TrimSpace(raw))
		if err != nil {
			return errors.New("invalid duration")
		}
		fv.SetInt(int64(d))
		return nil
	}
	switch fv.Kind() {
	case reflect.String:
		fv.SetString(raw)
	case reflect.Int, reflect.Int64:
		n, err := strconv.ParseInt(strings.TrimSpace(raw), 10, 64)
		if err != nil {
			return errors.New("invalid int")
		}
		fv.SetInt(n)
	case reflect.Bool:
		b, err := strconv.ParseBool(strings.TrimSpace(raw))
		if err != nil {
			return errors.New("invalid bool")
		}
		fv.SetBool(b)
	case reflect.Float64:
		x, err := strconv.ParseFloat(strings.TrimSpace(raw), 64)
		if err != nil {
			return errors.New("invalid float")
		}
		fv.SetFloat(x)
	case reflect.Slice:
		if fv.Type().Elem().Kind() != reflect.String {
			return errUnsupported
		}
		var items []string
		for _, p := range strings.Split(raw, ",") {
			if p = strings.TrimSpace(p); p != "" {
				items = append(items, p)
			}
		}
		fv.Set(reflect.ValueOf(items).Convert(fv.Type()))
	default:
		return errUnsupported
	}
	return nil
}

// readEnvFile parses simple KEY=VALUE lines ('#' comments, optional surrounding quotes).
// A missing file is not an error.
func readEnvFile(path string) (map[string]string, error) {
	out := map[string]string{}
	f, err := os.Open(path)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return out, nil
		}
		return nil, fmt.Errorf("config: %s okunamadı: %w", path, err)
	}
	defer func() { _ = f.Close() }()

	sc := bufio.NewScanner(f)
	for sc.Scan() {
		line := strings.TrimSpace(strings.TrimPrefix(sc.Text(), bom))
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		line = strings.TrimSpace(strings.TrimPrefix(line, "export "))
		k, val, ok := strings.Cut(line, "=")
		if !ok {
			continue
		}
		val = strings.TrimSpace(val)
		if len(val) >= 2 && (val[0] == '"' || val[0] == '\'') && val[len(val)-1] == val[0] {
			val = val[1 : len(val)-1]
		}
		out[strings.TrimSpace(k)] = val
	}
	if err := sc.Err(); err != nil {
		return nil, fmt.Errorf("config: %s okunamadı: %w", path, err)
	}
	return out, nil
}
