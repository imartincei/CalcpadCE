export interface PdfSettings {
  format: string;
  orientation: string;
  marginTop: string;
  marginBottom: string;
  marginLeft: string;
  marginRight: string;
  documentTitle: string;
  headerCenter: string;
  footerCenter: string;
  showPageNumbers: boolean;
  showDate: boolean;
  scale: number;
  dateTimeFormat: string;
}

export const DEFAULT_PDF_SETTINGS: Readonly<PdfSettings> = {
  format: 'Letter',
  orientation: 'portrait',
  marginTop: '0.75in',
  marginBottom: '0.75in',
  marginLeft: '0.5in',
  marginRight: '0.5in',
  documentTitle: '',
  headerCenter: '',
  footerCenter: '',
  showPageNumbers: true,
  showDate: true,
  scale: 1.0,
  dateTimeFormat: 'M/d/yyyy h:mm tt',
};
