import React from 'react';

export default function Home() {
  return (
    <div className="container">
      <main>
        <h1 className="title">
          Welcome to <a href="https://github.com/aliizzet75/lexwolf">LexWolf</a>
        </h1>

        <p className="description">
          Legal AI assistant for German lawyers
        </p>

        <div className="grid">
          <a href="/documents" className="card">
            <h3>Document Generator &rarr;</h3>
            <p>Generate legal documents with AI assistance</p>
          </a>

          <a href="/knowledge" className="card">
            <h3>Legal Knowledge Base &rarr;</h3>
            <p>Access our database of legal information</p>
          </a>

          <a href="/cases" className="card">
            <h3>Case Management &rarr;</h3>
            <p>Manage your legal cases efficiently</p>
          </a>

          <a href="/research" className="card">
            <h3>Legal Research &rarr;</h3>
            <p>Research laws and regulations</p>
          </a>
        </div>
      </main>

      <footer>
        <p>Powered by LexWolf AI</p>
      </footer>

      <style jsx>{`
        .container {
          min-height: 100vh;
          padding: 0 0.5rem;
          display: flex;
          flex-direction: column;
          justify-content: center;
          align-items: center;
        }

        main {
          padding: 5rem 0;
          flex: 1;
          display: flex;
          flex-direction: column;
          justify-content: center;
          align-items: center;
        }

        footer {
          width: 100%;
          height: 100px;
          border-top: 1px solid #eaeaea;
          display: flex;
          justify-content: center;
          align-items: center;
        }

        a {
          color: inherit;
          text-decoration: none;
        }

        .title a {
          color: #0070f3;
          text-decoration: none;
        }

        .title a:hover,
        .title a:focus,
        .title a:active {
          text-decoration: underline;
        }

        .title {
          margin: 0;
          line-height: 1.15;
          font-size: 4rem;
        }

        .title,
        .description {
          text-align: center;
        }

        .description {
          line-height: 1.5;
          font-size: 1.5rem;
        }

        .grid {
          display: flex;
          align-items: center;
          justify-content: center;
          flex-wrap: wrap;
          max-width: 800px;
          margin-top: 3rem;
        }

        .card {
          margin: 1rem;
          flex-basis: 45%;
          padding: 1.5rem;
          text-align: left;
          color: inherit;
          text-decoration: none;
          border: 1px solid #eaeaea;
          border-radius: 10px;
          transition: color 0.15s ease, border-color 0.15s ease;
        }

        .card:hover,
        .card:focus,
        .card:active {
          color: #0070f3;
          border-color: #0070f3;
        }

        .card h3 {
          margin: 0 0 1rem 0;
          font-size: 1.5rem;
        }

        .card p {
          margin: 0;
          font-size: 1.25rem;
          line-height: 1.5;
        }

        @media (max-width: 600px) {
          .grid {
            width: 100%;
            flex-direction: column;
          }
        }
      `}</style>

      <style jsx global>{`
        html,
        body {
          padding: 0;
          margin: 0;
          font-family: -apple-system, BlinkMacSystemFont, Avenir Next, Avenir,
            Helvetica, sans-serif;
        }

        * {
          box-sizing: border-box;
        }
      `}</style>
    </div>
  );
}