package config_test

import (
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/keopsfenks/modaryo/libs/go/modaryo/config"
)

type cfg struct {
	DBURL   string        `env:"DB_URL" required:"true"`
	Other   string        `env:"OTHER" required:"true"`
	Port    int           `env:"PORT" default:"8080"`
	Debug   bool          `env:"DEBUG" default:"false"`
	Ratio   float64       `env:"RATIO" default:"1.5"`
	Timeout time.Duration `env:"TIMEOUT" default:"5s"`
	Hosts   []string      `env:"HOSTS" default:"a, b ,c"`
	Big     int64         `env:"BIG" default:"9000000000"`
}

func TestConfig_MissingRequired_ErrorNamesVariable(t *testing.T) {
	t.Setenv("MODARYO_ENV", "production")
	_, err := config.Load[cfg]("MODARYO_")
	require.Error(t, err)
	assert.Contains(t, err.Error(), "Eksik yapılandırma")
	assert.Contains(t, err.Error(), "MODARYO_DB_URL")
	assert.Contains(t, err.Error(), "MODARYO_OTHER")
}

func TestConfig_Defaults(t *testing.T) {
	t.Setenv("MODARYO_ENV", "production")
	t.Setenv("MODARYO_DB_URL", "postgres://x")
	t.Setenv("MODARYO_OTHER", "o")
	c, err := config.Load[cfg]("MODARYO_")
	require.NoError(t, err)
	assert.Equal(t, "postgres://x", c.DBURL)
	assert.Equal(t, 8080, c.Port)
	assert.False(t, c.Debug)
	assert.Equal(t, 1.5, c.Ratio)
	assert.Equal(t, 5*time.Second, c.Timeout)
	assert.Equal(t, []string{"a", "b", "c"}, c.Hosts)
	assert.Equal(t, int64(9000000000), c.Big)
}

func TestConfig_ParsesValues(t *testing.T) {
	t.Setenv("MODARYO_ENV", "production")
	t.Setenv("MODARYO_DB_URL", "x")
	t.Setenv("MODARYO_OTHER", "o")
	t.Setenv("MODARYO_PORT", "9090")
	t.Setenv("MODARYO_DEBUG", "true")
	t.Setenv("MODARYO_TIMEOUT", "1m30s")
	t.Setenv("MODARYO_HOSTS", "x,y")
	c, err := config.Load[cfg]("MODARYO_")
	require.NoError(t, err)
	assert.Equal(t, 9090, c.Port)
	assert.True(t, c.Debug)
	assert.Equal(t, 90*time.Second, c.Timeout)
	assert.Equal(t, []string{"x", "y"}, c.Hosts)
}

func TestConfig_ParseError_NamesVariableNotValue(t *testing.T) {
	t.Setenv("MODARYO_ENV", "production")
	t.Setenv("MODARYO_DB_URL", "x")
	t.Setenv("MODARYO_OTHER", "o")
	t.Setenv("MODARYO_PORT", "s3cr3t-not-a-number")
	_, err := config.Load[cfg]("MODARYO_")
	require.Error(t, err)
	assert.Contains(t, err.Error(), "MODARYO_PORT")
	assert.NotContains(t, err.Error(), "s3cr3t")
}

func TestConfig_UnsupportedKind_Errors(t *testing.T) {
	type bad struct {
		M map[string]string `env:"M" default:"x"`
	}
	t.Setenv("MODARYO_ENV", "production")
	_, err := config.Load[bad]("MODARYO_")
	require.Error(t, err)
}

func writeEnvLocal(t *testing.T, content string) {
	t.Helper()
	dir := t.TempDir()
	require.NoError(t, os.WriteFile(filepath.Join(dir, ".env.local"), []byte(content), 0o600))
	t.Chdir(dir)
}

func TestConfig_EnvLocal_LoadedInDevAndEnvWins(t *testing.T) {
	writeEnvLocal(t, "# comment\n\nMODARYO_DB_URL=\"from-file\"\nMODARYO_OTHER='file-other'\nMODARYO_PORT=1234\n")
	t.Setenv("MODARYO_ENV", "")
	t.Setenv("MODARYO_PORT", "4321")
	c, err := config.Load[cfg]("MODARYO_")
	require.NoError(t, err)
	assert.Equal(t, "from-file", c.DBURL)
	assert.Equal(t, "file-other", c.Other)
	assert.Equal(t, 4321, c.Port, "gerçek ortam değişkeni dosyaya baskın olmalı")
}

func TestConfig_EnvLocal_IgnoredInProduction(t *testing.T) {
	writeEnvLocal(t, "MODARYO_DB_URL=from-file\nMODARYO_OTHER=o\n")
	t.Setenv("MODARYO_ENV", "production")
	_, err := config.Load[cfg]("MODARYO_")
	require.Error(t, err)
	assert.Contains(t, err.Error(), "MODARYO_DB_URL")
}
