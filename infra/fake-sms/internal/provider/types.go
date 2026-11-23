package provider

// İleti Merkezi JSON API zarfları. Alan adları dokümantasyondaki gibidir
// (ör. "receipents" yazım hatası dokümanda da böyledir). Şekiller
// testdata/*.json dosyalarında sabitlenmiştir.

type authentication struct {
	Key  string `json:"key"`
	Hash string `json:"hash"`
}

type sendRequest struct {
	Request struct {
		Authentication authentication `json:"authentication"`
		Order          struct {
			Sender       string `json:"sender"`
			IYS          string `json:"iys"`
			IYSList      string `json:"iysList,omitempty"`
			SendDateTime string `json:"sendDateTime,omitempty"`
			Message      struct {
				Text       string `json:"text"`
				Receipents struct {
					Number []string `json:"number"`
				} `json:"receipents"`
			} `json:"message"`
		} `json:"order"`
	} `json:"request"`
}

type reportRequest struct {
	Request struct {
		Authentication authentication `json:"authentication"`
		Order          struct {
			ID string `json:"id"`
		} `json:"order"`
	} `json:"request"`
}

type balanceRequest struct {
	Request struct {
		Authentication authentication `json:"authentication"`
	} `json:"request"`
}

type status struct {
	Code    int    `json:"code"`
	Message string `json:"message"`
}

type errorResponse struct {
	Response struct {
		Status status `json:"status"`
	} `json:"response"`
}

type sendResponse struct {
	Response struct {
		Status status `json:"status"`
		Order  struct {
			ID string `json:"id"`
		} `json:"order"`
	} `json:"response"`
}

type reportItem struct {
	Number string `json:"number"`
	Status int    `json:"status"`
}

type reportResponse struct {
	Response struct {
		Status status `json:"status"`
		Order  struct {
			ID          string       `json:"id"`
			Status      int          `json:"status"`
			Total       int          `json:"total"`
			Delivered   int          `json:"delivered"`
			Undelivered int          `json:"undelivered"`
			Waiting     int          `json:"waiting"`
			SubmitAt    string       `json:"submitAt"`
			SendAt      string       `json:"sendAt"`
			Sender      string       `json:"sender"`
			Price       float64      `json:"price"`
			Message     []reportItem `json:"message"`
		} `json:"order"`
	} `json:"response"`
}

type balanceResponse struct {
	Response struct {
		Status  status `json:"status"`
		Balance struct {
			Amount float64 `json:"amount"`
			SMS    int     `json:"sms"`
		} `json:"balance"`
	} `json:"response"`
}
